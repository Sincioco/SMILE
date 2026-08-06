namespace SMILE.Engine;

public sealed record FullLineCommentTokenValue(
    FullLineCommentMarker Marker,
    string Payload);

public sealed record SyntaxToken(
    SyntaxKind Kind,
    string Text,
    object? Value,
    TextSpan Span);
