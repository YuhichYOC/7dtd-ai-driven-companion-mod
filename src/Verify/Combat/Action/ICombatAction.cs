using CompanionAIVerify.Combat.Scene;

namespace CompanionAIVerify.Combat.Action;

internal interface ICombatAction
{
    internal string Name { get; }

    internal void Run(InfoHolder i);
}