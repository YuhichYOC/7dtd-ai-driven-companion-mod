using HarmonyLib;

namespace CompanionAIVerify
{
    // =============================================================================
    // テスト専用ハーネス: ゾンビの attackTarget をリーダー(GetPrimaryPlayer)へ固定する。
    //
    //   目的: ゾンビを殴るキャラが1人だけだと、ゾンビはその人物(=コンパニオン)へ向かってしまい、
    //         観察したい「ちょうどいい間合い」を維持できない。ゾンビの target 指定をすべて
    //         リーダーへ書き換えることで、ゾンビは常にリーダーへ向かい続ける。リーダーを静止
    //         させておけば、ゾンビはリーダーの前で足を止め、コンパニオンはその背後を追って
    //         リーチ内側で安定してバットを振る形になり、空振り/命中を落ち着いて観察できる。
    //
    //   実装:
    //     ・敵対のみ対象: EntityZombie / EntityEnemyAnimal / HostileHuman は EntityEnemy を継承
    //       （EntityZombie : EntityHuman : EntityEnemy : EntityAlive）。プレイヤーは EntityEnemy でない
    //       ため書き換えられない（リーダー/コンパニオンは対象外）。
    //     ・仕返し retarget も最終的に SetAttackTarget を通る（EAISetNearestEntityAsTarget:346,378 /
    //       revenge 経由でも同API）ため、本フック1点で一括抑制できる。AIの選択順に依存しない。
    //     ・ホスト(サーバ)限定。ゾンビAIはホスト側で走るので十分。client では SetAttackTarget 自体
    //       entityDistributer=null で使えない（World:468-477）ので、IsServer で弾く。
    //     ・null 指定(AIの標的解除)はそのまま通す＝自然な de-aggro は保つ。
    //
    //   コンパニオンの照準補正との非干渉:
    //     コンパニオン側の aim-assist は attackTarget を「直接フィールド代入」する（SetAttackTarget を
    //     使わない）。本フックは SetAttackTarget しか捕まえないため、コンパニオンの狙点には一切干渉しない。
    //     → ゾンビ=リーダーへ固定 / コンパニオン=ゾンビを狙う、が両立する。
    //
    //   安全性:
    //     Cfg.DebugPinTargetToLeader = false のとき最初の1行で return＝挙動ゼロ変化。ONの間は
    //     ワールド内の全敵対の標的がリーダーへ寄る点に注意（手動スポーンの検証用途を想定）。
    //     PatchAll() により自動登録（CompanionAIVerify.cs:159）。
    //
    //   ※ これは観察用スキャフォールドであり製品挙動ではない。検証後は false のまま運用する。
    // =============================================================================
    [HarmonyPatch(typeof(EntityAlive), "SetAttackTarget")]
    internal static class Patch_DebugPinTargetToLeader
    {
        // void SetAttackTarget(EntityAlive _attackTarget, int _attackTargetTime) の第1引数を書き換える。
        static void Prefix(EntityAlive __instance, ref EntityAlive _attackTarget)
        {
            if (!Cfg.DebugPinTargetToLeader) return;   // 既定OFF：完全無効
            if (_attackTarget == null) return;         // 標的解除(null)は素通し（自然な de-aggro を保つ）
            if (!(__instance is EntityEnemy)) return;  // 敵対のみ（プレイヤーは書き換えない）

            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm == null || !cm.IsServer) return;    // ホスト限定

            World world = __instance.world;
            if (world == null) return;

            EntityPlayer leader = world.GetPrimaryPlayer(); // ホストの主プレイヤー=リーダー（HostPathProbe:196 と同一の掴み方）
            if (leader == null || _attackTarget == leader) return;

            _attackTarget = leader;                    // 全ての標的指定をリーダーへ固定
        }
    }
}
