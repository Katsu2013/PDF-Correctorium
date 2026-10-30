using System.IO;
using System.Text.Json.Nodes;

namespace PdfCorrectorium.App.Services;

public sealed partial class PdfBookmarkService
{
    // Adds genuine structure elements and a complete reverse MCID parent map.
    // Original text/CMaps and visible content remain unchanged. /Alt is a human
    // language replacement; kana is never mislabeled as an IPA /Phoneme value.
    private async Task<int> WriteReadingStructureAsync(string pdfPath, PdfReadingPlan plan,
        IReadOnlyDictionary<int, string> pageReferences, JsonObject catalog, JsonObject updates,
        int nextObjectId, bool discardOldStructure, CancellationToken cancellationToken)
    {
        static bool HasKana(string value) => value.Any(character => character is >= '\u3040' and <= '\u30ff' or >= '\uff66' and <= '\uff9f');
        var cache = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        async Task<JsonNode?> Read(string reference)
        {
            if (updates["obj:" + reference]?["value"] is { } edited) return edited;
            if (!cache.TryGetValue(reference, out var value))
            {
                var result = await LoadLinkAnnotationValuesAsync(pdfPath, new JsonArray(reference), cancellationToken);
                value = result.GetValueOrDefault(reference); cache[reference] = value;
            }
            return value;
        }
        async Task<JsonNode?> Resolve(JsonNode? value)
        {
            if (value is JsonValue v && v.TryGetValue<string>(out var reference) && reference.EndsWith(" R", StringComparison.Ordinal)) return await Read(reference);
            return value;
        }
        string Add(JsonNode value) { var reference = $"{nextObjectId++} 0 R"; updates["obj:" + reference] = new JsonObject { ["value"] = value }; return reference; }
        void Put(string reference, JsonObject value) => updates["obj:" + reference] = new JsonObject { ["value"] = value.DeepClone() };
        static JsonArray AsArray(JsonNode? value) => value is JsonArray array ? array.DeepClone().AsArray() : value is null ? [] : new JsonArray(value.DeepClone());
        var oldReference = !discardOldStructure ? catalog["/StructTreeRoot"]?.GetValue<string>() : null;
        var root = oldReference is not null ? (await Read(oldReference))?.DeepClone().AsObject()
            ?? throw new InvalidDataException("既存のPDFタグ構造を読み取れませんでした。") : new JsonObject { ["/Type"] = "/StructTreeRoot" };
        var rootReference = oldReference ?? Add(root);
        var rootKids = AsArray(root["/K"]);
        var parents = new SortedDictionary<int, JsonNode?>();
        var visited = new HashSet<JsonNode>(ReferenceEqualityComparer.Instance);
        async Task ReadParentTree(JsonNode? value, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = await Resolve(value);
            if (node is not JsonObject tree || depth > 64 || !visited.Add(tree)) throw new InvalidDataException("PDFの親タグ索引が不正です。");
            if (tree["/Nums"] is JsonArray nums)
            {
                if (nums.Count % 2 != 0) throw new InvalidDataException("PDFの親タグ索引が不正です。");
                for (var i = 0; i < nums.Count; i += 2)
                { var key = nums[i]!.GetValue<int>(); if (key < 0 || !parents.TryAdd(key, nums[i + 1]?.DeepClone()) || parents.Count > 100000) throw new InvalidDataException("PDFの親タグ索引が不正です。"); }
            }
            if (tree["/Kids"] is JsonArray children) foreach (var child in children) await ReadParentTree(child, depth + 1);
        }
        if (oldReference is not null && root["/ParentTree"] is { } oldTree) await ReadParentTree(oldTree, 0);
        var nextParentKey = Math.Max(root["/ParentTreeNextKey"]?.GetValue<int>() ?? 0, parents.Count == 0 ? 0 : checked(parents.Keys.Max() + 1));
        var additions = new JsonArray(); var documentReference = Add(new JsonObject { ["/Type"] = "/StructElem", ["/S"] = "/Document", ["/P"] = rootReference, ["/K"] = additions });
        var refs = plan.Pages.Select(page => pageReferences.GetValueOrDefault(page.PageNumber)).Where(value => value is not null).Cast<string>().ToArray();
        var loaded = await LoadLinkAnnotationValuesAsync(pdfPath, new JsonArray(refs.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()), cancellationToken);
        foreach (var pair in loaded) cache[pair.Key] = pair.Value;
        foreach (var page in plan.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageReference = pageReferences[page.PageNumber];
            var pageValue = (await Read(pageReference))!.DeepClone().AsObject();
            if (discardOldStructure)
            {
                // Original annotation parent keys belonged to the discarded tree.
                // Do not let them alias the freshly allocated page parent keys.
                var annotations = await Resolve(pageValue["/Annots"]) as JsonArray;
                var directAnnotationChanged = false;
                if (annotations is not null)
                    foreach (var annotation in annotations)
                    {
                        if (annotation is JsonValue annotationReference && annotationReference.TryGetValue<string>(out var reference) && reference.EndsWith(" R", StringComparison.Ordinal) &&
                            await Read(reference) is JsonObject originalAnnotation && originalAnnotation.ContainsKey("/StructParent"))
                        {
                            var editedAnnotation = originalAnnotation.DeepClone().AsObject(); editedAnnotation.Remove("/StructParent"); Put(reference, editedAnnotation);
                        }
                        else if (annotation is JsonObject directAnnotation) directAnnotationChanged |= directAnnotation.Remove("/StructParent");
                    }
                if (directAnnotationChanged && pageValue["/Annots"] is JsonValue annotationArrayReference &&
                    annotationArrayReference.TryGetValue<string>(out var arrayReference) && arrayReference.EndsWith(" R", StringComparison.Ordinal))
                    updates["obj:" + arrayReference] = new JsonObject { ["value"] = annotations!.DeepClone() };
            }
            if (page.Contents.Count == 0)
            {
                if (discardOldStructure) { pageValue.Remove("/StructParents"); Put(pageReference, pageValue); }
                continue;
            }
            var oldKey = !discardOldStructure ? pageValue["/StructParents"]?.GetValue<int>() : null;
            var key = oldKey ?? nextParentKey++;
            var parentArray = oldKey is not null && parents.TryGetValue(key, out var oldParents)
                ? (await Resolve(oldParents))?.DeepClone().AsArray() ?? throw new InvalidDataException("ページの親タグ配列を読み取れません。") : new JsonArray();
            if (page.NewMcids.Any(id => id < parentArray.Count && parentArray[id] is not null))
                throw new InvalidDataException("新しい読み方の識別子が既存タグと重複しています。元の構造を保持するため保存を中止しました。");
            var pageKids = new JsonArray();
            var sectionReference = Add(new JsonObject { ["/Type"] = "/StructElem", ["/S"] = "/Sect", ["/P"] = documentReference, ["/Pg"] = pageReference, ["/K"] = pageKids });
            foreach (var content in page.Contents)
            {
                var ids = content.Mcids.Distinct().Order().ToArray();
                if (ids.Length == 0 || ids[0] < 0 || ids[^1] > 1000000) throw new InvalidDataException("読み方の文字識別子が不正です。");
                var existing = ids.Where(id => id < parentArray.Count && parentArray[id] is not null)
                    .Select(id => parentArray[id]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToArray();
                if (existing.Length > 0)
                {
                    // Never replace the original structure tree or attach a line's
                    // reading to a larger paragraph that also owns other text.
                    if (content.AlternativeText is null) continue;
                    if (existing.Length != 1 || ids.Any(id => id >= parentArray.Count || parentArray[id] is null))
                        throw new InvalidDataException($"{page.PageNumber}ページ: 読み方の対象が複数の既存PDFタグに分かれています。既存構造を壊さないため出力を中止しました。");
                    var element = (await Read(existing[0]))?.DeepClone().AsObject() ?? throw new InvalidDataException("読み方の既存タグを読み取れません。");
                    var children = AsArray(element["/K"]);
                    var owned = new List<int>();
                    foreach (var child in children)
                    {
                        if (child is JsonValue scalar && scalar.TryGetValue<int>(out var id)) owned.Add(id);
                        else if (child is JsonObject mcr && mcr["/MCID"] is JsonValue mcid && mcid.TryGetValue<int>(out id) &&
                            mcr["/Stm"] is null && (mcr["/Pg"] is null || mcr["/Pg"]!.GetValue<string>() == pageReference)) owned.Add(id);
                        else throw new InvalidDataException("読み方の対象タグが他の内容も含むため、安全に更新できません。");
                    }
                    if (!owned.Distinct().Order().SequenceEqual(ids) || element["/Pg"] is { } pg && pg.GetValue<string>() != pageReference)
                        throw new InvalidDataException("読み方の対象タグが他の内容も含むため、安全に更新できません。");
                    element["/Alt"] = "u:" + content.AlternativeText;
                    if (HasKana(content.AlternativeText)) element["/Lang"] = "u:ja-JP";
                    Put(existing[0], element); continue;
                }
                var leaf = new JsonObject { ["/Type"] = "/StructElem", ["/S"] = "/P", ["/P"] = sectionReference, ["/Pg"] = pageReference,
                    ["/K"] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) };
                if (content.AlternativeText is not null) { leaf["/Alt"] = "u:" + content.AlternativeText; if (HasKana(content.AlternativeText)) leaf["/Lang"] = "u:ja-JP"; }
                var leafReference = Add(leaf); pageKids.Add(leafReference);
                foreach (var id in ids) { while (parentArray.Count <= id) parentArray.Add((JsonNode?)null); parentArray[id] = leafReference; }
            }
            if (pageKids.Count > 0) additions.Add(sectionReference);
            parents[key] = parentArray; pageValue["/StructParents"] = key; Put(pageReference, pageValue);
        }
        if (additions.Count > 0) rootKids.Add(documentReference);
        var nums = new JsonArray();
        foreach (var pair in parents) { nums.Add(pair.Key); nums.Add(pair.Value?.DeepClone()); }
        root["/K"] = rootKids; root["/ParentTree"] = Add(new JsonObject { ["/Nums"] = nums }); root["/ParentTreeNextKey"] = nextParentKey;
        Put(rootReference, root); catalog["/StructTreeRoot"] = rootReference;
        var markInfo = (await Resolve(catalog["/MarkInfo"]))?.DeepClone() as JsonObject ?? new JsonObject();
        markInfo["/Marked"] = true; catalog["/MarkInfo"] = markInfo;
        return nextObjectId;
    }
}