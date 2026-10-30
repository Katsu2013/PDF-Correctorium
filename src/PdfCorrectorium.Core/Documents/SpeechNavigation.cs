namespace PdfCorrectorium.Core.Documents;

public enum SpeechNavigationUnit { Paragraph, Line }
public readonly record struct SpeechCursor(int Paragraph, int Line);
public readonly record struct SpeechMove(SpeechCursor Cursor, int PageDelta);

public static class SpeechNavigation
{
    public static SpeechMove Move(IReadOnlyList<SpeechUtterance> paragraphs, SpeechCursor current, int direction, SpeechNavigationUnit unit)
    {
        if (paragraphs.Count == 0) return new(default, Math.Sign(direction));
        var paragraph = Math.Clamp(current.Paragraph, 0, paragraphs.Count - 1);
        var line = Math.Clamp(current.Line, 0, paragraphs[paragraph].Spans.Count - 1);
        direction = Math.Sign(direction);
        if (direction == 0) return new(new(paragraph, line), 0);
        if (unit == SpeechNavigationUnit.Line)
        {
            var next = line + direction;
            if (next >= 0 && next < paragraphs[paragraph].Spans.Count) return new(new(paragraph, next), 0);
        }
        paragraph += direction;
        if (paragraph < 0 || paragraph >= paragraphs.Count) return new(current, direction);
        return new(new(paragraph, unit == SpeechNavigationUnit.Line && direction < 0 ? paragraphs[paragraph].Spans.Count - 1 : 0), 0);
    }
}
