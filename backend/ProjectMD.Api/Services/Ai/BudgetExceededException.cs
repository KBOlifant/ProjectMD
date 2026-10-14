namespace ProjectMD.Api.Services.Ai;

// Which daily cap was hit.
public enum BudgetScope
{
    // This user's own daily token cap (Ai:DailyTokenCapPerUser).
    User,

    // The cap shared by everyone (Ai:DailyTokenCapGlobal).
    Global,
}

// Thrown by ILlmBudgetGuard when a call would take the day's AI spending over a cap. The pipeline
// turns it into the basic, AI-free document when Ai:FallbackOnBudget is true; otherwise it reaches
// the controller, which answers 429 with reason "budget".
public sealed class BudgetExceededException : Exception
{
    // Which cap was hit.
    public BudgetScope Scope { get; }

    // Creates the exception for the given cap. The message is safe to show to a user.
    public BudgetExceededException(BudgetScope scope)
        : base(MessageFor(scope))
    {
        Scope = scope;
    }

    // The friendly sentence for each cap (no numbers, nothing internal).
    private static string MessageFor(BudgetScope scope) => scope == BudgetScope.User
        ? "You have used up today's AI writing allowance. The basic scan still works, and the allowance resets at midnight UTC."
        : "AI writing has reached its daily capacity for everyone. Please try again after midnight UTC.";
}
