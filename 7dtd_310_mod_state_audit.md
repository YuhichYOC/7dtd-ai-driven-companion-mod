# 7DTD 3.1.0 MOD 情報取得 棚卸し表

対象バージョン: **3.1.0 "Henpocalypse"**  
依存: ~~SCore のみ~~ 一旦は依存対象なしで進める  
調査手段: `Assembly-CSharp.dll`（＋必要に応じ `-firstpass`）の逆コンパイル  
ビルドターゲット（後工程）: **netstandard2.0**（Unity/Mono互換。`net10` 不可）  

---

## 使い方

1. `ilspycmd -p -o .\decompiled "<...>\Managed\Assembly-CSharp.dll"` で全量ダンプ。
2. 各行の「grep起点（当たり）」を手がかりに、`decompiled\` を検索して実体を特定する。
3. 見つけたら **取得元クラス.メンバ / 型 / 取得コスト / SCoreラッパー / 3.1.0確認** を埋める。
4. 取れなかった項目は末尾の「取得不能 → 補完候補」へ移す（＝VLM／推定で埋める設計判断へ回す）。

確認欄の記法: `☐` 未確認 / `☑` 3.1.0のダンプで実物を確認済み。
取得コスト欄: `field`（フィールド読み・毎フレーム可） / `query`（列挙・探索。頻度を絞る） / `calc`（自前算出）。
備考の `(file:line)` は逆コンパイルソース上の根拠位置。

---

## A. プレイヤー状態（自機）

| # | 設計入力 | grep起点（当たり） | 取得元クラス.メンバ | 型 | 取得コスト | SCoreラッパー | 3.1.0確認 | 備考 |
|---|---|---|---|---|---|---|---|---|
| A1 | プレイヤー座標 | `EntityPlayerLocal` / `EntityAlive.position` | `Entity.GetPosition()` | `Vector3` | field | | ☑ | `public Vector3 position` フィールドも直読可 (Entity:182, 2538)。EntityPlayerLocal は Entity を継承 |
| A2 | HP | `EntityAlive.Health` / `Stats` | `EntityAlive.Health` | `int` | field | | ☑ | `(int)Stats.Health.Value` 由来。上限 `GetMaxHealth()` (EntityAlive:1913, 3320) |
| A3 | スタミナ | `Stats.Stamina` | `EntityAlive.Stamina` | `float` | field | | ☑ | `Stats.Stamina.Value` 由来。姿勢判断の補助指標（任意）(EntityAlive:1925) |
| A4 | 装填弾数 | `holdingItemItemValue` / `.Meta` | `EntityAlive.inventory.holdingItemItemValue.Meta` | `int` | field | | ☑ | **確定**: 銃の `ItemValue.Meta` = マガジン残弾 (ItemActionRanged:501, 529 / Inventory:156) |
| A5 | 予備弾数 | `bag`＋`inventory` の弾個数 | `entity.bag.GetItemCount(ammo)` ＋ `entity.inventory.GetItemCount(ammo)` | `int` | calc | | ☑ | **bag だけでなく inventory も数える**。ゲーム側も両方参照 (ItemActionRanged:502-503)。`ammo` は A7 で取得 |
| A6 | インベントリ全体 | `bag`(`Bag`) / `inventory`(`Inventory`) | `entity.bag`（型 `Bag`）/ `entity.inventory`（型 `Inventory`） | `ItemStack[]` | field | | ☑ | **小文字 `bag`**（Entity:300）。中身は `Bag.items`（Bag:17）or `GetSlots()`。大文字 `Bag` は EntityLockContext の別物 |
| A7 | 現在保持している銃の弾種別 | 銃 `ItemValue` の選択弾種 | `holdingItemItemValue.SelectedAmmoTypeIndex` → 弾種 `ItemValue` | `ItemValue`（byte索引経由） | field | | ☑ | `MagazineItemNames` と併せて選択中弾種を解決 (ItemActionRanged:537, 545-557)。A5 の `ammo` 材料 |

> 弾まわりの接地まとめ: **装填=`holdingItemItemValue.Meta`** / **予備=`bag`+`inventory` の `GetItemCount(ammo)`** / **弾種=`holdingItemItemValue.SelectedAmmoTypeIndex`**。3つとも `ItemActionRanged` の数行に揃っている。

## B. 近接エンティティ / 脅威検知（第0層入口）

| # | 設計入力 | grep起点（当たり） | 取得元クラス.メンバ | 型 | 取得コスト | SCoreラッパー | 3.1.0確認 | 備考 |
|---|---|---|---|---|---|---|---|---|
| B1 | エンティティ列挙 | `World.GetEntitiesInBounds` / `GetEntitiesAround` | `World.GetEntitiesInBounds(Entity _excludeEntity, Bounds _aabbOfEntity)` | `List<Entity>` | query | | ☑ | **第1引数=除外エンティティ**（プレイヤーを渡し自分を結果から外す）、**第2引数=`Bounds`(AABB)**。下の推奨も参照 (World:2324) |
| B1' | （毎フレーム用の推奨形） | 使い回しリストに書き込む版 | `World.GetEntitiesAround(EntityFlags _mask, Vector3 _pos, float _radius, List<Entity> _list)` | `void`（_list に格納） | query | | ☑ | **毎フレームのGC回避**に有利（呼び出し側リスト再利用）。半径指定で済む脅威走査向き。生存のみ絞る `_isAlive` 付き Bounds版も有 (World:2406, 2341)。マスクは None/Zombie/AIHearing 程度と粗い＝ゾンビの粗フィルタ止まり |
| B2 | 相対距離 | B1結果 + `self.position` | `Entity.GetDistance(Entity _other)` | `float` | calc | | ☑ | 閾値比較だけなら `GetDistanceSq` で `sqrt` を省ける (Entity:2012, 2018) |
| B3 | エンティティ種別 | `EntityEnemy` / `EntityZombie` / `entityClass` | **主**: `is` 型テスト（`EntityZombie` / `EntityEnemyAnimal` / `EntityAnimal`）。**粗フィルタ補助**: `is EntityEnemy` or `EntityClass.bIsEnemyEntity` | `Entity`（型テスト）/ `bool`（フラグ） | field | | ☑ | 静的kind軸（B5の動的敵対性とは別軸）。継承確定→備考／下記注記。動物=型で敵対性確定、人間=`bIsEnemyEntity` or B5 必須の非対称 |
| B4 | sleeper状態 | `IsSleeper` / `IsSleeping` | **現在睡眠**: `EntityAlive.IsSleeping`（未覚醒の主軸）。**種別フラグ**: `EntityAlive.IsSleeper`（スリーパーか否か） | `bool` | field | | ☑ | **別メンバ注意**: `IsSleeper`(種別, :438) と `IsSleeping`(現在睡眠, :440) は別物。未覚醒判定は `IsSleeping==true`。`IsSleeperPassive`(:442) も有。C章参照 |
| B5 | aggro / 攻撃対象 | `GetAttackTarget()` / `GetRevengeTarget()` | `EntityAlive.attackTarget` | `EntityAlive` | field, 友軍か判定するには計算が必要 | | ☑ | 対象がローカルプレイヤー : attackTarget.entityId == EntityPlayerLocal.entityId、友軍他プレイヤー : EntityPlayerLocal.party.MemberList.exists(e => e.entityId == 友軍 entityId) |
| B6 | alert / investigate状態 | AI task系（investigate/alert） | **明示的な状態フィールドは無し**（`sleepingOrWakingUp => IsSleeping` に畳込み, EntityAlive:1404）。`GetSleeperDisturbedLevel(dist, lightLevel)` は状態でなく瞬間刺激量の純関数 | `int`（0/1/2, 先行指標のみ） | calc（補助） | | ☑ | **状態の主軸には使えない**（下記詳細）。覚醒中は合成条件で接地（C章）。`GetSleeperDisturbedLevel`(EntityAlive:2583) は第0層の「起こされつつある」先行指標として補助利用 |

> **B6の結論（会話「B6」で決着）**: `EntityAlive.GetSleeperDisturbedLevel`(2583) は距離と光量から視覚刺激量を算出する純関数（`calc`）で、内部状態を持たない。呼び出しは `if (theEntity.IsSleeping)` の内側限定（EAISetNearestEntityAsTarget:271, 297）＝遷移を駆動する側で、遷移後の「起きて調査中／警戒中」を表せない。加えて視覚（光量）チャネル専用で、音経路（`noisePlayerVolume` vs `sleeperNoiseToWake`/`sleeperNoiseToSense`）を取りこぼす。戻り値1（groan）は「感知したがまだ起きない」＝意味的には「未覚醒だが刺激されている」で、設計の「覚醒中」とは別物。よって**状態主軸ではなく先行指標**として扱う。

## C. 三値覚醒状態のマッピング（設計モデル → game側の真実）

第0層の状態機械を game の実状態に接地させるための対応付け。**ここが写像できるかが最重要**。→ **B3/B6会話とソース裏取りで確定**。

| 設計状態 | 対応するgame側条件（確定） | 参照メンバ（B列から） | 3.1.0確認 | 備考 |
|---|---|---|---|---|
| 未覚醒 | `IsSleeping == true` | B4 | ☑ | スリーパーが睡眠中。`IsSleeper`(種別)ではなく `IsSleeping`(現在睡眠, EntityAlive:440)が主軸 |
| 覚醒中 | `IsSleeping == false` ∧ 攻撃対象がローカルプレイヤーでない（合成） | B4(`IsSleeping==false`) ∧ B5 | ☑ | エンジンに明示的中間状態は無く合成で近似。`IsSleeper && !IsSleeping`(EntityAlive:5845)が「起きたスリーパー」の実在条件で、この合成の裏付け |
| 交戦中 | `GetAttackTarget()`/`GetRevengeTarget()` == ローカルプレイヤー | B5 | ☑ | 対象がプレイヤー以外なら覚醒中扱い |

> **検証メモ（決着済み）**: `EntityAlive.cs:1404` の `public bool sleepingOrWakingUp => IsSleeping;` が示すとおり、
> エンジンは「起床遷移中」を**独立状態として持たず `IsSleeping` に畳み込んでいる**。旧メモが懸念した
> 「明示的な中間状態が無いかもしれない」は、少なくともこの範囲で**その通り**だった。したがって「覚醒中」は
> （`IsSleeping == false` ∧ 攻撃対象がローカルプレイヤーでない）の**合成条件で確定**させる。
> `GetSleeperDisturbedLevel`(2583) は光量のみ・瞬間値・要 `IsSleeping` の純関数のため、状態主軸ではなく
> 「スリーパーがまさに起こされつつある」を検知する**先行指標**として第0層で使う（戻り値 1/2 = 覚醒へ傾き）。

## D. 失効例外の抑止条件（リーダー文脈）

「ユーザー宣言モードの失効」（交戦モード → 移動モードの自動遷移）を **抑止する例外条件** を game 側で判定するための入力（メモ.md「ユーザー宣言モード失効の例外条件」）。`suppressRevert()` の OR 条件を構成する。**いずれもリーダーの `EntityPlayer` から読む**（自機ではなくリーダー側の文脈）。

| # | 設計入力 | grep起点（当たり） | 取得元クラス.メンバ | 型 | 取得コスト | SCoreラッパー | 3.1.0確認 | 備考 |
|---|---|---|---|---|---|---|---|---|
| D1 | 建物探索中（POI進入） | `enteredPrefab` / `prefabInfoEntered` | `EntityPlayer.enteredPrefab`（真偽は `prefabInfoEntered`） | `PrefabInstance` | field | | ☑ | POI進入フラグ。`prefab`(現在地POI, :109) / `prefabInfoEntered`(:113) / `prefabTimeIn`(:115) 併存。ダンジョン系限定なら `prefab.prefab.DifficultyTier > 0`(EntityPlayer:745) (EntityPlayer:111) |
| D2 | クエスト進行中 | `QuestJournal.ActiveQuest` | `EntityPlayer.QuestJournal.ActiveQuest` | `Quest`（`ActiveQuest` は null 可） | field | | ☑ | `QuestJournal`(:151)。null 判定で進行中。POIクリア系に絞るなら `ActiveQuest.QuestClass` で種別確認 (EntityPlayer:525, 527) |
| D3 | 近傍に未覚醒の脅威が存在 | B1 ＋ `IsSleeping` | (B1列挙) ＋ `EntityAlive.IsSleeping` | `bool` | calc | | ☑ | B1/B2 を再利用。未覚醒の主軸は `IsSleeping`(EntityAlive:440)＝**B4/C「未覚醒」行と同一材料**。種別フラグ `IsSleeper` ではない |

> **接地メモ**: D1/D2 はリーダーの `EntityPlayer` 直読（追従中はコンパニオン自身の `enteredPrefab` でも近似可だが、意味的にはリーダー基準）。D3 は B4/C の未覚醒判定をそのまま近傍走査に流用するだけで、追加コストはほぼゼロ。**この判断の実装優先度は下げる**（メモ.md 準拠）。

---

## SCore 突き合わせ欄

各行の「SCoreラッパー」列を埋める前に、SCore の公開ソース（GitHub / sphereii系）で
以下の観点を確認する。埋まった分だけ第0層の自前実装を減らせる。

- [ ] 既存の状態アクセサ（プレイヤー/エンティティ状態の取得ヘルパ）
- [ ] エンティティ探索・センシング系ユーティリティ
- [ ] AI/ターゲティングまわりのフックや拡張点
- [ ] Harmony パッチ適用の作法（SCoreが提供する土台）

---

## 取得不能 → 補完候補（VLM / 推定）

逆コンパイルで「game側から直接読めない」と確定した情報をここへ集める。
これが前段設計の「MODで取れない → VLMや推定で補う」判断リストになる。

| 情報 | なぜ取れないか | 補完手段の候補 | 優先度 |
|---|---|---|---|
| （例）遮蔽物越しの視線可否の厳密判定 | レイキャストは可だがコスト高 | 幾何近似 / 低頻度query | |
| スリーパーの明示的な「起床遷移中」状態 | エンジンが独立状態を持たず `sleepingOrWakingUp => IsSleeping` に畳込み（EntityAlive:1404） | B4×B5の合成条件で近似済（C章）＋`GetSleeperDisturbedLevel`を先行指標に | 済（合成で接地） |
| | | | |

---

## 参照メモ

- ダンプ: `ilspycmd -p -o .\decompiled "<install>\7DaysToDie_Data\Managed\Assembly-CSharp.dll"`
- 難読化なし（クラス名・メンバ名は原型）。GUI（ILSpy/dnSpyEx）で参照追跡すると特定が速い。
- 上記「当たり」は調査の起点であり、3.1.0の正確なシグネチャはダンプ実物で確定させること。
- 罠メモ: `Entity.bag`（小文字＝インベントリ）と `EntityLockContext.Bag`（大文字＝ロック文脈）は別物。case-insensitive grep で取り違えないこと。
- 罠メモ2: `IsSleeper`（種別フラグ, EntityAlive:438）と `IsSleeping`（現在睡眠状態, :440）は別メンバ。未覚醒判定に使うのは `IsSleeping`。

### B3 分類ロジック（確定した継承チェーンと注意点）

継承チェーン（各クラス宣言で確認済）:

```
Entity - EntityAlive - EntityAnimal              (非敵対的動物)
                     |
                     + EntityEnemy - EntityEnemyAnimal   (敵対的動物)
                                   |
                                   + EntityHuman - EntityZombie  (ゾンビ)
```

- `EntityAnimal` と `EntityEnemyAnimal` は `EntityAlive` 直下の**別枝＝互いに素**。動物の敵対/非敵対は型テストだけで曖昧さゼロに決まる。
- ハザードは**ゾンビ／人間境界**へ移動: `EntityZombie : EntityHuman` なので `is EntityHuman` はゾンビも真になる。汎用分類器は**最派生先行**で書く（`EntityZombie` を `EntityHuman` より必ず前に）。
- 三値（ゾンビ/敵対動物/非敵対動物）だけなら3つは互いに素で順不同でも通るが、規律として最派生先行を推奨。

```csharp
switch (e)
{
    case EntityZombie z:        /* ゾンビ */                          break;
    case EntityEnemyAnimal ea:  /* 敵対的動物 */                      break;
    case EntityHuman h:         /* 生身の人間の敵（ゾンビは上で除外済）*/ break;
    case EntityEnemy en:        /* その他敵性（将来クラスの保険）*/     break;
    case EntityAnimal a:        /* 非敵対的動物 */                    break;
    case EntityPlayer p:        /* プレイヤー */                      break;
}
```

- 非対称の注意: 動物は**型で敵対性が確定**（`EntityAnimal`＝非敵対 / `EntityEnemyAnimal`＝敵対）。一方 `EntityHuman` からゾンビを除いた「生身の人間」は型だけでは敵対性未確定（バンディット等）で、`EntityClass.bIsEnemyEntity`(EntityAlive:4993, 3051) か B5 の攻撃対象で判定する。
- B3は「そのエンティティが何か」の**静的kind軸**。「いま自分に襲ってきているか」の動的敵対性は B5（`GetAttackTarget()==ローカルプレイヤー`）が担当。B3に動的敵対まで背負わせないのが三値モデルをきれいに接地させるコツ。

### 検証済みログ（3.1.0 逆コンパイル実物）

- A1 `Entity.GetPosition()` : `Vector3` (Entity:2538) / `position` フィールド (Entity:182)
- A2 `EntityAlive.Health` : `int`、`GetMaxHealth()` (EntityAlive:1913, 3320)
- A3 `EntityAlive.Stamina` : `float` (EntityAlive:1925)
- A4 `holdingItemItemValue.Meta` = 装填残弾 (ItemActionRanged:501, 529 / Inventory:156)
- A5 予備弾 = `bag.GetItemCount(ammo)` + `inventory.GetItemCount(ammo)` (ItemActionRanged:502-503)
- A6 `entity.bag`→`Bag.items` : `ItemStack[]` (Entity:300 / Bag:17)
- A7 `holdingItemItemValue.SelectedAmmoTypeIndex` + `MagazineItemNames` (ItemActionRanged:537, 545-557)
- B1 `World.GetEntitiesInBounds(除外Entity, Bounds)` : `List<Entity>` (World:2324) / `GetEntitiesAround(flags, pos, radius, list)` (World:2406) / `_isAlive` 版 (World:2341)
- B2 `Entity.GetDistance(Entity)` : `float` / `GetDistanceSq` (Entity:2012, 2018)
- B3 継承チェーン確定: `EntityAnimal:EntityAlive` / `EntityEnemy:EntityAlive` / `EntityEnemyAnimal:EntityEnemy` / `EntityHuman:EntityEnemy` / `EntityZombie:EntityHuman`（各クラス宣言）。分類=`is EntityZombie`/`is EntityEnemyAnimal`/`is EntityAnimal` 主 ＋ `EntityClass.bIsEnemyEntity`(EntityAlive:4993, 3051) 粗フィルタ補助。順序ハザードはゾンビ/人間境界（最派生先行）
- B4 `IsSleeper`(種別フラグ, EntityAlive:438) と `IsSleeping`(現在睡眠, :440) は別メンバ。`IsSleeperPassive`(:442) も有。未覚醒の主軸は `IsSleeping`。`IsSleeper && !IsSleeping`(:5845) が「起きたスリーパー」の実在条件
- B6 `GetSleeperDisturbedLevel(dist, lightLevel)` : `int`(0/1/2)、瞬間刺激量の純関数（状態でない, EntityAlive:2583）。呼び出しは `if (IsSleeping)` 内(EAISetNearestEntityAsTarget:271, 297)。`sleepingOrWakingUp => IsSleeping`(EntityAlive:1404) より明示的中間状態なし
- C 三値マッピング確定: 未覚醒=`IsSleeping==true`(B4) / 覚醒中=`IsSleeping==false ∧ 攻撃対象≠ローカルプレイヤー`(B4∧B5, 合成) / 交戦中=`GetAttackTarget()==ローカルプレイヤー`(B5)
- D1 `EntityPlayer.enteredPrefab` : `PrefabInstance`（POI進入, EntityPlayer:111）。`prefab`(:109) / `prefabInfoEntered`(:113) / `prefabTimeIn`(:115)。ダンジョン限定 `prefab.prefab.DifficultyTier > 0`(EntityPlayer:745)
- D2 `EntityPlayer.QuestJournal`(:151) → `.ActiveQuest`（null で進行中, EntityPlayer:525）＋ `.QuestClass`(:527)
- D3 近傍未覚醒 = B1列挙 ＋ `EntityAlive.IsSleeping`(:440)。B4/C 未覚醒行と同一材料（追加コストほぼゼロ）

## 訂正・確定追記（2026-08-17, ver0.1 脅威検知スライス実機検証）

### B5 訂正: 攻撃対象の読み取りはクライアントでは GetAttackTargetLocal()
- **旧**: `EntityAlive.attackTarget` / `GetAttackTarget()`、`attackTarget.entityId == EntityPlayerLocal.entityId`
- **新（クライアント接地）**: `EntityAlive.GetAttackTargetLocal()` を使う。
  `tgt = e.GetAttackTargetLocal(); engaged := tgt != null && tgt.entityId == EntityPlayerLocal.entityId`
- **根拠**: 敵はコンパニオン(クライアント)から見て全て remote。攻撃対象の設定は
  サーバ側 `SetAttackTarget` 内 `if(!isEntityRemote)` でのみ実行され、`NetPackageSetAttackTarget`
  を追跡プレイヤーへ送信して**サーバ側フィールド `attackTarget`** を設定する(EntityAlive:5930-5936)。
  クライアントは受信して `SetAttackTargetClient` → **`attackTargetClient`**(:722) を埋める(:5938-5941)。
  `GetAttackTargetLocal()` は remote 時 `attackTargetClient` を返す(:5900-5907)。
  → クライアントで `attackTarget`/`GetAttackTarget()` 直読は remote 敵に対し**常に空**になり得る。**罠**。

### C章 訂正（B5連動）
- 覚醒中: `IsSleeping == false ∧ GetAttackTargetLocal() のentityId ≠ ローカルプレイヤー`
- 交戦中: `GetAttackTargetLocal()?.entityId == EntityPlayerLocal.entityId`
- （`GetRevengeTarget()` も同様にサーバ側フィールド由来のため、クライアント判定には使わない）

### B4 補強: IsSleeping はクライアント直読可（レプリケート確認）
- `attackTarget` と異なり client 専用フィールドは無い。サーバ起床時に
  `NetPackageSleeperWakeup` を送信し(EntityAlive:2651-2654)、**同一フィールド `IsSleeping`**(:440)が
  クライアントで更新される。→ 直読で安全。実機でも未覚醒→起床の遷移が視線挙動に正しく反映。

### 実機検証結果（ログ 2026-08-17 17:14–17:33）
- 分類・状態の全ケース確認: Zombie / EnemyAnimal(蛇) を視線対象化、PassiveAnimal(ウサギ) は
  hostiles に非計上、未覚醒スリーパーは `sleeping` に計上され視線対象外、起床で `Awakening` へ遷移し対象化。
- **Engaged が entityId で対象を判別できることを確認**: 野良ゾンビが 6.2→2.0m で `Engaged` を持続後、
  至近で `Awakening` へクリーンに反転（103-110行→111行以降）。単発欠落でなく持続反転のため、
  **ゾンビの攻撃対象がコンパニオン→リーダーへ移った**解釈が最有力。「対象非null」でなく「自分が対象」を
  見ている証左。
- **友軍判定の据え置きが実データで可視化**: 上記反転中、リーダーが襲われていても companion 視点は
  `Awakening` 止まり。→ 交戦スライスでは `Engaged` を**友軍(リーダー/party)狙いにも拡張**する判断が要る
  （`EntityPlayerLocal.party.MemberList` で友軍 entityId 照合、B5 旧メモの友軍式を GetAttackTargetLocal 版で復活）。

## E. 交戦パス（攻撃実行）— ver0.3 交戦スライスで接地

第0層の攻撃実行を game の実処理に接地させる対応付け。すべて 3.1.0 逆コンパイル実物で裏取り済み。

| # | 設計入力 | 取得元クラス.メンバ | 型 | 取得コスト | 3.1.0確認 | 備考 |
|---|---|---|---|---|---|---|
| E1 | 攻撃の発火 | `EntityAlive.Attack(bool _isReleased)` → `UseHoldingItem(0, _isReleased)` | `bool` | — | ☑ | `Attack(false)`=press／`Attack(true)`=release。press で `Attacking=true`＝スイング開始 (EntityAlive:6142, 6164) |
| E2 | 攻撃ケイデンス | press を毎フレーム張るだけ | — | — | ☑ | `canStartAttack` が `Time.time - lastUseTime < 60/APM + 0.1` で自動律速。多重発火なし。ブロック時は無害に return (ItemActionDynamicMelee:358) |
| E3 | 実ヒットの適用 | `Inventory` が `holdingItem.OnHoldingUpdate(holdingItemData)` を毎フレーム駆動 | — | — | ☑ | press 後、hold 中に自動でヒット適用。コンパニオンは EntityPlayerLocal＝inventory が tick する (Inventory:403) |
| E4 | 攻撃レイの原点/方向 | `EntityPlayerLocal.GetLookRay()` / `GetMeleeRay()`（camera 由来） | `Ray` | — | ☑ | `playerCamera.ViewportPointToRay(center)`。**entity rotation 直接でなく camera forward**。FPV時 GetMeleeRay=GetLookRay (EPL:3847, 3869) |
| E5 | facing→攻撃レイの操舵 | `EntityPlayerLocal.SetRotation(Vector3)` → `m_vp_FPCamera.Angle` 更新 | `void` | — | ☑ | facingスライスの SetRotation がそのままカメラ＝攻撃レイを動かす。別経路不要 (EPL:2310-2321) |
| E6 | 近接/遠距離の判別 | `holdingItem.Actions[0] is ItemActionRanged`（遠距離）／それ以外＝近接 | `bool` | field | ☑ | 継承: `ItemActionRanged:ItemActionAttack` / `ItemActionDynamicMelee:ItemActionDynamic` / `ItemActionMelee:ItemActionAttack`（各宣言） |
| E7 | 近接射程 | `holdingItem.Actions[0].Range` | `float` | field | ☑ | 素手/取得不可時は 2.0m フォールバック。`Range` は `ItemAction` の public field (ItemAction:46) |

### E-a. bFirstPersonView は実行時決定（デフォルト true は保証されない）— ★ 交戦の前提

`public bool bFirstPersonView = true;`(EPL:395) は**あくまで初期値**。spawn/respawn で上書きされる:

- `AfterPlayerRespawn`(EPL:3715): `AttachedToEntity != null` → `SetFirstPersonView(false, …)`。
  そうでなければ `SwitchToPreferredCameraMode(true)`(EPL:3729) が走る。
- `SwitchToPreferredCameraMode`(EPL:3645):
  - `IsEditMode() || CameraRestrictionMode==0` → `SetFirstPersonView(bPreferFirstPerson, …)`。
    `bPreferFirstPerson` は `OptionsGfxDefaultFirstPersonCamera`(EPL:1282) 由来＝**コンパニオンPCのグラフィック設定**。
  - `CameraRestrictionMode==1` → FPV強制(true) / `==2` → TPV強制(false)＝**サーバ設定**。

→ **コンパニオンの `bFirstPersonView` は「クライアントのグラフィック設定」または「サーバの CameraRestrictionMode」で false になり得る**。
よって「交戦の手前で実ログ出力して確定」は必須確認。ver0.3 は
`engage-precheck: bFirstPersonView={} TPCam={} camPassed={}` を初回/変化時に出力する。

### E-b. bFirstPersonView==true で攻撃ゲートが全消しになる根拠

- `CharacterCameraAngleValid`(EPL:5969): `if (bFirstPersonView || vp_FPCamera.Locked3rdPerson) return eTPCameraCheckResult.Pass;`
  → FPV なら LineOfSight/Angle チェックに一切入らず即 Pass。
- `canStartAttack`(ItemActionDynamicMelee:337): TPCamera 分岐は `holdingEntity is EntityPlayerLocal { bFirstPersonView: false }` **限定**。
  FPV なら分岐ごとスキップ＝カメラ角で弾かれない。
- `eTPCameraCheckResult.Pass == 0`（enum 既定値, eTPCameraCheckResult.cs）＋ `TPCameraCheckResult` は
  `LateUpdate`(EPL:2955) で毎フレーム再評価。→ 二重に安全（未評価でも既定 Pass）。

**結論**: `bFirstPersonView==true` を実ログで確認できれば、攻撃系に Harmony パッチは不要（監査の予測どおり）。
false だった場合の対処は `SetFirstPersonView(true, false)` の一発自己修復（ver0.3 の `Cfg.ForceFirstPerson`）。

### E-c. netsync

ダメージのレプリケーションは下流 `DamageEntity → SendToServer(NetPackageDamageEntity)` に内包され、
入力層(`Attack`/`UseHoldingItem`)には無い。→ クライアントから直接 `Attack()` を呼んでも netsync-safe（既存確定事項の再確認）。

### E-d. 本スライスで意図的に据え置いた項目

- **遠距離武器の発砲**: `Actions[0] is ItemActionRanged` はログのみで撃たない（弾/リロード/aggro 管理は別スライス）。
- **脅威への接近（engage maneuver）**: 射程内に来た脅威のみ叩く。距離を詰める移動 AI は未実装。
- **友軍(リーダー/party)狙い脅威の交戦対象化**: `Engaged` 判定を友軍 entityId へ拡張する件（既存の据え置き）は交戦対象選択と併せて別スライス。
- **ピッチ精度**: `FaceTarget3D` は eye≈+1.5m / aim≈対象+0.9m の概算。低い脅威(蛇/クローラ)での実当たりを実機で要確認。

## F. 発砲パス（遠距離攻撃実行）— ver0.4 発砲スライスで接地

近接(Section E)に続き、`ItemActionRanged` の発火を game 実処理に接地。すべて 3.1.0 逆コンパイル実物で裏取り。

| # | 設計入力 | 取得元クラス.メンバ | 型 | 3.1.0確認 | 備考 |
|---|---|---|---|---|---|
| F1 | 発砲の発火 | `Attack(false)` → `UseHoldingItem(0,false)` → `ItemActionRanged.ExecuteAction(_,false)` → `TryExecuteAction` | — | ☑ | press=発火(セミ)/開始(オート)。release=`triggerReleased`(ItemActionRanged:1118,1143) |
| F2 | ★セミオートの発火条件 | `bInitialPress`（押下の立ち上がりフレームのみ） | `bool` | ☑ | `flag = bInitialPress && (rapidTrigger \|\| burstCount==1)`(1160)。**press 張りっぱなしは初弾のみ**。連射は press↔release サイクル必須。melee と真逆 |
| F3 | オートの発火 | `flag2 = burstCount==0 \|\| curBurstCount<burstCount` ＋ Delay 律速 | — | ☑ | フルオートは hold で連射。`Delay=60/RPM`(731)。バースト数 `GetBurstCount`=`BurstRoundCount`(1386,1388) |
| F4 | 発射レート | `itemActionDataRanged.Delay = 60/RoundsPerMinute` | `float` | ☑ | セミ(burstCount==1)は Delay ゲート(1188)を通らず内部律速なし＝**ドライバ側でケイデンス制御が必要** |
| F5 | 弾数(装填) | `holdingItemItemValue.Meta`（A4 再利用） | `int` | ☑ | `ConsumeAmmo`(1264) で発砲毎に減。press 前後の Meta 差で**実発砲を検出**可能 |
| F6 | 弾切れ→自動リロード | 空撃ち時 `CanReload`→`requestReload`→`GameManager.ItemReloadServer` | — | ☑ | (1234-1246,617-623)。`AutoReload` 銃は最終弾後にも要求(1304-1306)。→ **ドライバ側リロード管理不要** |
| F7 | リロード中の抑止 | `Reloading()` 中は press を吸収（`m_LastShotTime=time` で return） | — | ☑ | (1182-1185)。トリガー引き続けても暴発せず、明けたら再開 |
| F8 | 発砲の狙点/レイ | `GetExecuteActionTarget`→`holdingEntity.GetLookRay()`（camera 由来）＋`getDirectionOffset`(スプレッド) | `Ray` | ☑ | 着弾は camera＝`SetRotation` で操舵(ItemActionRanged:1579,1604 / EPL:2310)。bFPV=true でレーザー代替分岐(1585)に落ちない |
| F9 | ヘッドショット狙点 | `Entity.getHeadPosition()`(=`emodel.GetHeadPosition()` 頭ボーン) | `Vector3` | ☑ | `dir = target.getHeadPosition() - self.getHeadPosition()` が最精度(Entity:2642)。null時 `position + up*GetEyeHeight()` フォールバック内蔵 |
| F10 | netsync | `ItemActionEffectsServer`(発砲エフェクト/ダメージをサーバ複製) | — | ☑ | (1286)。ranged も直接 `Attack()` で netsync-safe。melee の DamageEntity 経路に相当 |

### F-a. ドライバ設計（ver0.4）

- **press(フレームN)→release(フレームN+1) を `RangedFireIntervalSec` ごとに回す**単一サイクル。
  セミオートは press 立ち上がりで1発、オート/バーストも即 release により1発ずつの安全ケイデンスに揃う
  （初スライスの目的＝暴発なく確実に「発砲を見る」）。F4 の無律速セミオートに対する外側スロットル。
- **狙点は頭部**（F9）。近接は SphereRadius 許容で従来どおり胴狙い +0.9m のまま（E章）。
- **弾切れはゲート自動リロード任せ**（F6/F7）。ドライバは Meta 差(F5)で発砲/空/リロード完了をログ化するのみ。
- 距離ゲート `RangedMaxEngageMeters=18m`（走査半径20m の内側）。`ItemAction.Range` は銃だと大きい/小さいが
  まちまちなので発砲判定には使わず固定上限で予測可能に。

### F-b. 意図的に据え置いた項目（次スライス候補）

- **LoS（遮蔽）判定**: 壁越しは `fireShot` の Voxel.Raycast が壁に当たり弾を浪費。開けた場所前提。
- **フルオートの hold 連射 / バースト最適化**: 現状は全銃を1発/tick に均している。武器種別の連射解放は別途。
- **ケイデンスの RPM 追従**: 現状固定 0.4s。`Delay=60/RPM`(F4) を読んで武器なりに合わせる余地。
- **リロード完了ログの取りこぼし**: 順序上「fire mag=N」の mag 跳ね上がりでリロードは可視化されるが、
  明示的 "reload done" は出ないケースあり（動作は正常、ログ表現のみ）。
- **弾/狙点の使い分け（頭 vs 胴）**: 遠距離ヘッドショット狙いは頭固定。装甲や状況での胴狙い切替は将来。
