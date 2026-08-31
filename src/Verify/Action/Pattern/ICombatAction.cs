using CompanionAIVerify.Action.Scene;

namespace CompanionAIVerify.Action.Pattern;

internal interface ICombatAction
{
    internal string Name { get; }

    internal void Run(InfoHolder i);
}