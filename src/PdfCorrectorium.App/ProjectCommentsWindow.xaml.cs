using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PdfCorrectorium.App.Services;
using PdfCorrectorium.Core.Documents;

namespace PdfCorrectorium.App;

public partial class ProjectCommentsWindow : Window
{
    private readonly ProjectTargetReference _target;
    private readonly List<ProjectComment> _otherComments;
    private readonly List<ProjectTag> _tags;
    private bool _loading;

    public ProjectCommentsWindow(ProjectTargetReference target, IReadOnlyList<ProjectComment> comments, IReadOnlyList<ProjectTag> tags)
    {
        _target = target;
        _otherComments = comments.Where(item => !SameTarget(item.Target, target)).ToList();
        _tags = tags.ToList();
        Comments = new ObservableCollection<ProjectComment>(comments.Where(item => SameTarget(item.Target, target)).OrderByDescending(item => item.UpdatedAtUtc));
        InitializeComponent();
        CommentList.ItemsSource = Comments;
        if (Comments.Count > 0) CommentList.SelectedIndex = 0;
        LocalizationService.Apply(this);
    }

    public ObservableCollection<ProjectComment> Comments { get; }
    public IReadOnlyList<ProjectComment> ResultComments { get; private set; } = [];
    public IReadOnlyList<ProjectTag> ResultTags { get; private set; } = [];

    private void New_OnClick(object sender, RoutedEventArgs e)
    {
        SaveEditor();
        var comment = new ProjectComment { Target = _target, Body = "新しいコメント" };
        Comments.Insert(0, comment);
        CommentList.SelectedItem = comment;
        BodyBox.Focus();
        BodyBox.SelectAll();
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (CommentList.SelectedItem is not ProjectComment comment) return;
        var index = CommentList.SelectedIndex;
        Comments.Remove(comment);
        CommentList.SelectedIndex = Comments.Count == 0 ? -1 : Math.Min(index, Comments.Count - 1);
    }

    private void CommentList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.OfType<ProjectComment>().FirstOrDefault() is { } previous) SaveEditor(previous);
        LoadEditor(CommentList.SelectedItem as ProjectComment);
    }

    private void LoadEditor(ProjectComment? comment)
    {
        _loading = true;
        try
        {
            var body = comment?.Body ?? string.Empty;
            if (!string.Equals(BodyBox.Text, body, StringComparison.Ordinal))
                BodyBox.Text = body;
            ImportanceBox.SelectedIndex = comment is null ? 2 : (int)comment.Importance;
            ResolvedBox.IsChecked = comment?.State == ProjectCommentState.Resolved;
            var tags = comment is null ? string.Empty : string.Join(", ", _tags.Where(tag => comment.TagIds.Contains(tag.Id)).Select(tag => tag.Name));
            if (!string.Equals(TagsBox.Text, tags, StringComparison.Ordinal))
                TagsBox.Text = tags;
        }
        finally { _loading = false; }
    }

    private void Editor_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading) SaveEditor();
    }

    private void SaveEditor(ProjectComment? item = null)
    {
        if (_loading || (item ?? CommentList.SelectedItem) is not ProjectComment comment) return;
        var names = TagsBox.Text.Split([',', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var tagIds = new List<Guid>();
        foreach (var name in names)
        {
            var tag = _tags.FirstOrDefault(candidate => candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (tag is null)
            {
                tag = new ProjectTag { Name = name };
                _tags.Add(tag);
            }
            tagIds.Add(tag.Id);
        }
        var updated = comment with
        {
            Body = BodyBox.Text,
            Importance = (ProjectCommentImportance)Math.Clamp(ImportanceBox.SelectedIndex, 0, 4),
            State = ResolvedBox.IsChecked == true ? ProjectCommentState.Resolved : ProjectCommentState.Open,
            TagIds = tagIds,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var index = Comments.IndexOf(comment);
        if (index >= 0) Comments[index] = updated;
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        SaveEditor();
        if (Comments.Any(item => string.IsNullOrWhiteSpace(item.Body)))
        {
            MessageBox.Show(this, "コメント本文を入力してください。", "コメントとタグ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var usedTagIds = _otherComments.Concat(Comments).SelectMany(item => item.TagIds).ToHashSet();
        ResultComments = _otherComments.Concat(Comments).ToArray();
        ResultTags = _tags.Where(tag => usedTagIds.Contains(tag.Id)).ToArray();
        DialogResult = true;
    }

    private static bool SameTarget(ProjectTargetReference left, ProjectTargetReference right) =>
        left.Kind == right.Kind && left.PageId == right.PageId && left.ObjectId == right.ObjectId &&
        left.CharacterStart == right.CharacterStart && left.CharacterLength == right.CharacterLength && left.ExternalKey == right.ExternalKey;
}
