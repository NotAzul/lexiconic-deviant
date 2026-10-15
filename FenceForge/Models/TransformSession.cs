namespace FenceForge.Models;

public sealed class TransformSession
{
    public TransformSession(string originalSource)
    {
        OriginalSource = originalSource;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string OriginalSource { get; }

    public TransformDefinition? SubmittedTransform { get; private set; }

    public void Submit(TransformDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (SubmittedTransform is not null)
        {
            throw new InvalidOperationException("This session already has a submitted transformation.");
        }

        SubmittedTransform = definition;
    }
}
