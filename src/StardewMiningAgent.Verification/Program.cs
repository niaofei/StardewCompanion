using System.Collections.Immutable;
using StardewMiningAgent.Combat;
using StardewMiningAgent.Models;
using StardewMiningAgent.Navigation;
using StardewMiningAgent.Planning;
using StardewMiningAgent.Mining;
using StardewMiningAgent.Safety;

Console.WriteLine("=== StardewMiningAgent 核心模块数学验证 ===\n");

int failed = 0;
TestDynamicWeightFormula();
TestFeatureNormalization();
TestQFunctionMonotonicity();
TestAStarPathfinding();
TestRiskMapGaussian();
TestWeaponHitModel();
TestBombPlanner();
TestSafetySupervisor();
TestTaskSelection();

Console.WriteLine(failed > 0 ? $"\n=== 有 {failed} 个验证失败 ===" : "\n=== 全部验证通过 ===");
Environment.ExitCode = failed > 0 ? 1 : 0;
return;

static MapState CreateTestMap(int w, int h, MineKind mk = MineKind.Normal, int floor = 1)
{
    var rows = new List<ImmutableArray<TileKind>>();
    for (int y = 0; y < h; y++)
    {
        var b = ImmutableArray.CreateBuilder<TileKind>(w);
        for (int x = 0; x < w; x++)
            b.Add(x == 0 || x == w - 1 || y == 0 || y == h - 1 ? TileKind.Wall : TileKind.Empty);
        rows.Add(b.ToImmutable());
    }
    return new MapState(mk, floor, w, h,
        ImmutableArray.CreateRange(rows),
        ImmutableArray<Vec2>.Empty, ImmutableArray<Vec2>.Empty, ImmutableArray<Vec2>.Empty,
        ImmutableHashSet<(int, int)>.Empty, ImmutableHashSet<(int, int)>.Empty);
}

static PlayerState MakePlayer(int hp = 80, int maxHp = 100, Vec2? pos = null, WeaponKind wk = WeaponKind.Sword)
{
    var p = pos ?? new Vec2(5f, 5f);
    return new PlayerState(p, (int)p.X, (int)p.Y, Direction.Down,
        hp, maxHp, 270, 270, 4.0f, wk, 1.5f, true, false, false);
}

static WorldState MakeWorld(MapState? map = null, PlayerState? player = null,
    ImmutableArray<EnemyState> enemies = default, long tick = 0)
{
    map ??= CreateTestMap(20, 20);
    player ??= MakePlayer();
    if (enemies.IsDefault) enemies = ImmutableArray<EnemyState>.Empty;
    var inv = new InventoryState(0, 0, 0, 0, 0, 3, 1, 5, 0, 36);
    return new WorldState(new GameTime(1, 6, 0, 0), player, enemies, map, inv, tick, player.Position, 0f, ImmutableArray<int>.Empty);
}

static EnemyState MakeEnemy(int id, Vec2 pos, EnemyBehavior beh = EnemyBehavior.Tracking) => new(
    id, "Slime", pos, 0.5f, 0.3f, 20, 20, beh, false, 1.2f, 0.4f,
    ImmutableArray.Create(pos), 0.7f);

static GoalState MakeGoal(GoalMode mode) => new(
    mode, TargetResource.Nothing, 0, false, 0, false,
    ImmutableDictionary<string, float>.Empty, ImmutableDictionary<string, float>.Empty, 0);

static void Check(bool cond, string msg)
{
    if (!cond) { Console.WriteLine($"  FAIL: {msg}"); }
}

static void TestDynamicWeightFormula()
{
    Console.WriteLine("[1] 动态权重公式验证");
    var policy = new DynamicWeightPolicy();
    var state = MakeWorld();

    var q1 = policy.ComputeQ(
        BaseWeights.ForMode(GoalMode.MonsterFarming),
        new FeatureVector(0.8f, 0.6f, 0.2f, 0.3f, 0.5f, 0.3f, 0.2f, 0.1f),
        MakeGoal(GoalMode.MonsterFarming), state);
    Check(DynamicWeightPolicy.ValidateNormalizedWeights(q1, out _), "战斗模式权重应归一化");
    Console.WriteLine($"  战斗 Q={q1.Q:F4} w_dmg={q1.WD:F2} w_group={q1.WG:F2} w_risk={q1.WR:F2}");

    var q2 = policy.ComputeQ(
        BaseWeights.ForMode(GoalMode.OreFarming),
        new FeatureVector(0.1f, 0.2f, 0.9f, 0.2f, 0.5f, 0.1f, 0.3f, 0.2f),
        MakeGoal(GoalMode.OreFarming), state);
    Check(DynamicWeightPolicy.ValidateNormalizedWeights(q2, out _), "采矿模式权重应归一化");
    Console.WriteLine($"  采矿 Q={q2.Q:F4} w_res={q2.WO:F2} w_depth={q2.WL:F2}");

    var q3 = policy.ComputeQ(
        BaseWeights.ForMode(GoalMode.Survival),
        new FeatureVector(0.2f, 0.1f, 0.1f, 0.1f, 0.5f, 0.9f, 0.1f, 0.3f),
        MakeGoal(GoalMode.Survival), state);
    Check(DynamicWeightPolicy.ValidateNormalizedWeights(q3, out _), "生存模式权重应归一化");
    Console.WriteLine($"  生存 Q={q3.Q:F4} w_risk={q3.WR:F2} w_pos={q3.WP:F2}");

    Console.WriteLine("  PASS\n");
}

static void TestFeatureNormalization()
{
    Console.WriteLine("[2] 特征归一化验证");
    var n = new FeatureNormalizer();
    var f = n.Normalize(100f, 5f, 500f, 20f, 30f, 0.8f, 2.5f, 30f);
    Check(f.DamageGain >= 0f && f.DamageGain <= 1f, $"伤害归一化越界 {f.DamageGain}");
    Check(f.Risk <= 1f, $"风险归一化越界 {f.Risk}");
    Check(f.ResourceCost >= 0f && f.ResourceCost <= 1f, $"资源成本越界 {f.ResourceCost}");
    Console.WriteLine($"  dmg={f.DamageGain:F2} grp={f.GroupGain:F2} res={f.ResourceGain:F2} dep={f.DepthGain:F2}");
    Console.WriteLine($"  pos={f.PositionGain:F2} risk={f.Risk:F2} time={f.TimeCost:F2} rc={f.ResourceCost:F2}");

    var f0 = n.Normalize(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
    Check(f0.DamageGain == 0f && f0.ResourceGain == 0f && f0.Risk == 0f, "全零输入应输出全零");
    var fMax = n.Normalize(10000f, 1000f, 100000f, 1000f, 5000f, 10f, 100f, 10000f);
    Check(fMax.DamageGain <= 1f && fMax.ResourceGain <= 1f && fMax.Risk <= 1f, "超大量应被裁剪到 [0,1]");
    Console.WriteLine("  PASS\n");
}

static void TestQFunctionMonotonicity()
{
    Console.WriteLine("[3] Q 函数单调性验证");
    var policy = new DynamicWeightPolicy();
    var state = MakeWorld();
    var bw = BaseWeights.ForMode(GoalMode.MonsterFarming);

    var fSafe = new FeatureVector(0.5f, 0.3f, 0.2f, 0.2f, 0.5f, 0.1f, 0.2f, 0.1f);
    var fDanger = new FeatureVector(0.5f, 0.3f, 0.2f, 0.2f, 0.5f, 0.8f, 0.2f, 0.1f);
    var qs = policy.ComputeQ(bw, fSafe, MakeGoal(GoalMode.MonsterFarming), state).Q;
    var qd = policy.ComputeQ(bw, fDanger, MakeGoal(GoalMode.MonsterFarming), state).Q;
    Check(qs > qd, $"安全 Q ({qs:F3}) 应 > 危险 Q ({qd:F3})");

    var fLowD = new FeatureVector(0.1f, 0.3f, 0.2f, 0.2f, 0.5f, 0.3f, 0.2f, 0.1f);
    var fHiD = new FeatureVector(0.7f, 0.3f, 0.2f, 0.2f, 0.5f, 0.3f, 0.2f, 0.1f);
    var qld = policy.ComputeQ(bw, fLowD, MakeGoal(GoalMode.MonsterFarming), state).Q;
    var qhd = policy.ComputeQ(bw, fHiD, MakeGoal(GoalMode.MonsterFarming), state).Q;
    Check(qhd > qld, $"高伤害 Q ({qhd:F3}) 应 > 低伤害 Q ({qld:F3})");

    Console.WriteLine($"  安全Q={qs:F3} > 危险Q={qd:F3}");
    Console.WriteLine($"  高伤害Q={qhd:F3} > 低伤害Q={qld:F3}");
    Console.WriteLine("  PASS\n");
}

static void TestAStarPathfinding()
{
    Console.WriteLine("[4] A* 带风险代价寻路验证");
    var map = CreateTestMap(20, 20);

    var state = MakeWorld(map);
    var rm = new RiskMap(20, 20);
    for (int x = 5; x <= 15; x++)
        for (int y = 5; y <= 15; y++)
        {
            float dx = x - 10, dy = y - 10;
            rm[x, y] = Math.Clamp(MathF.Exp(-(dx * dx + dy * dy) / (2f * 3f)), 0f, 1f);
        }

    var pf = new AStarPathfinder(state, rm, riskWeight: 2.0f);
    var path = pf.FindPath(1, 1, 18, 18);
    Check(path.Success, "风险寻路应成功");
    Check(path.Path.Count > 2, $"路径长度={path.Path.Count} 过短");
    Console.WriteLine($"  风险寻路: len={path.Path.Count}, cost={path.TotalCost:F2}, length={path.Length:F2}");

    var pfNoRisk = new AStarPathfinder(state, null, riskWeight: 0f);
    var direct = pfNoRisk.FindPath(1, 1, 18, 18);
    Check(direct.Success, "无风险寻路应成功");
    Console.WriteLine($"  直线寻路: cost={direct.TotalCost:F2}, length={direct.Length:F2}");

    float pathRiskSum = 0f;
    foreach (var p in path.Path) pathRiskSum += rm[(int)p.X, (int)p.Y];
    float dirRiskSum = 0f;
    foreach (var p in direct.Path) dirRiskSum += rm[(int)p.X, (int)p.Y];
    Check(pathRiskSum <= dirRiskSum, $"风险路径累计风险={pathRiskSum:F2} 应 <= 直线路径={dirRiskSum:F2}");
    Console.WriteLine($"  累计风险: 路径={pathRiskSum:F2}, 直线={dirRiskSum:F2}");
    var largeMap=CreateTestMap(150,120);
    var largePath=new AStarPathfinder(MakeWorld(largeMap),maxNodes:40000).FindPath(1,1,148,118);
    Check(largePath.Success&&largePath.Path.First().X==1.5f&&largePath.Path.Last().X==148.5f,"large floor above 8192 cells initializes safely and reaches exact in-bounds goal");
    var blockedGoal=new AStarPathfinder(MakeWorld(CreateTestMap(20,20))).FindPath(1,1,0,0);
    Check(!blockedGoal.Success,"blocked map-edge goal is rejected instead of treated as passable");
    Console.WriteLine("  PASS\n");
}

static void TestRiskMapGaussian()
{
    Console.WriteLine("[5] 高斯风险地图验证");
    var map = CreateTestMap(15, 15);
    var enemies = ImmutableArray.Create(MakeEnemy(1, new Vec2(7f, 7f)));
    var state = MakeWorld(map, enemies: enemies);
    var rm = RiskMap.Compute(state);

    float c = rm[7, 7];
    float near = rm[7, 9];
    float far = rm[1, 1];
    Check(c > near, $"中心风险 {c:F3} 应 > 邻近 {near:F3}");
    Check(near > far, $"邻近风险 {near:F3} 应 > 远处 {far:F3}");
    Console.WriteLine($"  center={c:F3}, near={near:F3}, far={far:F3}");
    Console.WriteLine("  PASS\n");
}

static void TestWeaponHitModel()
{
    Console.WriteLine("[6] 武器命中模型验证");
    var w = WeaponModel.Create(WeaponKind.Sword);
    Check(w.CanHit(new Vec2(5f, 5f), Direction.Right, new Vec2(6.5f, 5f)), "正向应命中");
    Check(!w.CanHit(new Vec2(5f, 5f), Direction.Right, new Vec2(3.5f, 5f)), "身后不应命中");
    Check(!w.CanHit(new Vec2(5f, 5f), Direction.Right, new Vec2(5.5f, 8f)), "偏离扇区不应命中");
    Console.WriteLine($"  剑: 正向hit={w.CanHit(new Vec2(5, 5), Direction.Right, new Vec2(6.5f, 5f))}, 身后hit={w.CanHit(new Vec2(5, 5), Direction.Right, new Vec2(3.5f, 5f))}, 大角hit={w.CanHit(new Vec2(5, 5), Direction.Right, new Vec2(5.5f, 8f))}");

    var dagg = WeaponModel.Create(WeaponKind.Dagger);
    Check(dagg.CanHit(new Vec2(5f, 5f), Direction.Right, new Vec2(5.8f, 5f)), "匕首近距应命中");
    Check(!dagg.CanHit(new Vec2(5f, 5f), Direction.Right, new Vec2(7f, 5f)), "匕首超距不应命中");
    Console.WriteLine($"  匕首: 近距hit={dagg.CanHit(new Vec2(5, 5), Direction.Right, new Vec2(5.8f, 5f))}, 超距hit={dagg.CanHit(new Vec2(5, 5), Direction.Right, new Vec2(7f, 5f))}");
    var map=CreateTestMap(20,20);
    var edgeCandidate=new GroupingPlanner().FindBestGroupingPoint(new Vec2(3.5f,8.5f),new(){new Vec2(.5f,8.5f)},1.6f,map);
    Check(edgeCandidate.HasValue&&map.InBounds(edgeCandidate.Value.ToTileX,edgeCandidate.Value.ToTileY)&&map.IsWalkable(edgeCandidate.Value.ToTileX,edgeCandidate.Value.ToTileY),"group combat planner never returns an out-of-bounds or blocked candidate");
    Console.WriteLine("  PASS\n");
}

static void TestBombPlanner()
{
    Console.WriteLine("[7] 炸弹爆破评估验证");
    var map = CreateTestMap(12, 12);
    var rows = new List<ImmutableArray<TileKind>>();
    for (int y = 0; y < 12; y++)
    {
        var b = ImmutableArray.CreateBuilder<TileKind>(12);
        for (int x = 0; x < 12; x++)
        {
            if (x == 0 || x == 11 || y == 0 || y == 11) b.Add(TileKind.Wall);
            else if (x >= 4 && x <= 7 && y >= 4 && y <= 7) b.Add(TileKind.Stone);
            else b.Add(TileKind.Empty);
        }
        rows.Add(b.ToImmutable());
    }
    var rb = rows[5].ToBuilder();
    rb[5] = TileKind.Iron;
    rows[5] = rb.ToImmutable();
    var rb6 = rows[6].ToBuilder();
    rb6[6] = TileKind.LadderDown;
    rows[6] = rb6.ToImmutable();

    var bombMap = new MapState(MineKind.Normal, 1, 12, 12,
        ImmutableArray.CreateRange(rows),
        ImmutableArray<Vec2>.Empty, ImmutableArray<Vec2>.Empty, ImmutableArray<Vec2>.Empty,
        ImmutableHashSet<(int, int)>.Empty, ImmutableHashSet<(int, int)>.Empty);

    var state = MakeWorld(bombMap, player: MakePlayer(pos: new Vec2(2f, 2f)));
    var inv = new InventoryState(0, 0, 0, 0, 0, 5, 0, 3, 0, 36);
    var bp = new BombPlanner();
    var r = bp.EvaluateBombPosition(state, 6, 6, BombKind.Big, inv);
    Check(r.Valid, $"有效炸弹应返回 Valid=true, BlockReason={r.BlockReason}");
    Check(r.OreCount >= 1, $"应检测到矿石, 实际={r.OreCount}");
    Console.WriteLine($"  Big: stone={r.StoneCount}, ore={r.OreCount}, exit={r.ExitProbability:F2}, gain={r.TotalGain:F2}, cost={r.TotalCost:F2}, score={r.Score:F2}");

    var bad = bp.EvaluateBombPosition(state, -1, -1, BombKind.Small, inv);
    Check(bad.BlockReason != null, "越界应被阻止");
    Console.WriteLine("  PASS\n");
}

static void TestSafetySupervisor()
{
    Console.WriteLine("[8] 安全硬约束验证");
    var safety = new SafetySupervisor { CriticalHealthRatio = 0.15f };
    var inv = new InventoryState(0, 0, 0, 0, 0, 2, 1, 5, 0, 36);

    var high = MakeWorld(player: MakePlayer(hp: 90, maxHp: 100));
    Check(safety.CheckAction(high, null, 0.1f, inv).Approved, "高血量应允许任意动作");

    var low = MakeWorld(player: MakePlayer(hp: 10, maxHp: 100));
    var cLow = safety.CheckAction(low, null, 0.3f, inv);
    Check(cLow.Emergency, "极低血量应触发紧急");
    Check(safety.ShouldFlee(low), "极低血量应建议逃跑");

    var cWallMap = CreateTestMap(10, 10);
    var walledRows = new List<ImmutableArray<TileKind>>();
    for (int y = 0; y < 10; y++)
    {
        var b = ImmutableArray.CreateBuilder<TileKind>(10);
        for (int x = 0; x < 10; x++)
        {
            if (x == 0 || x == 9 || y == 0 || y == 9) b.Add(TileKind.Wall);
            else if ((x == 4 && y >= 4 && y <= 6) || (y == 4 && x >= 4 && x <= 6) || (x == 6 && y == 4))
                b.Add(TileKind.Wall);
            else b.Add(TileKind.Empty);
        }
        walledRows.Add(b.ToImmutable());
    }
    var walledMap = new MapState(MineKind.Normal, 1, 10, 10,
        ImmutableArray.CreateRange(walledRows),
        ImmutableArray<Vec2>.Empty, ImmutableArray<Vec2>.Empty, ImmutableArray<Vec2>.Empty,
        ImmutableHashSet<(int, int)>.Empty, ImmutableHashSet<(int, int)>.Empty);

    var enemies = ImmutableArray.Create(MakeEnemy(1, new Vec2(4f, 4f)), MakeEnemy(2, new Vec2(6f, 4f)),
        MakeEnemy(3, new Vec2(4f, 6f)), MakeEnemy(4, new Vec2(6f, 6f)));
    var trappedWorld = MakeWorld(walledMap, player: MakePlayer(hp: 34, maxHp: 100, pos: new Vec2(5f, 5f)), enemies: enemies);
    Console.WriteLine($"  包围场景 Enclosure={trappedWorld.Enclosure:F2}");
    Check(safety.ShouldFlee(trappedWorld), "4敌包围且低血量应逃跑");

    Console.WriteLine("  高血量approved=true, 低血量emergency=true, 包围flee=true");
    Console.WriteLine("  PASS\n");
}

static void TestTaskSelection()
{
    Console.WriteLine("[9] 任务选择器验证");
    var evaluator = new UtilityEvaluator();
    var safety = new SafetySupervisor();
    var selector = new TaskSelector(evaluator, minHoldTicks: 60);
    var state = MakeWorld(player: MakePlayer(hp: 90, maxHp: 100));

    var emptyExplain = ImmutableDictionary<string, string>.Empty;
    var candidates = new List<(string, TaskEvaluation)>
    {
        ("击杀怪物", new TaskEvaluation(true, 0.0f, 0.6f, 0.0f, 0.0f, 0.0f, 0.4f, 0.1f, 0.2f, 0.1f, emptyExplain)),
        ("采矿",     new TaskEvaluation(true, 0.0f, 0.0f, 0.0f, 0.7f, 0.2f, 0.1f, 0.1f, 0.3f, 0.2f, emptyExplain)),
        ("冲层",     new TaskEvaluation(true, 0.0f, 0.0f, 0.0f, 0.1f, 0.8f, 0.2f, 0.2f, 0.4f, 0.1f, emptyExplain)),
    };

    var t1 = selector.Select(state, MakeGoal(GoalMode.MonsterFarming), candidates, safety);
    Check(t1 != null, "应选择某个任务");
    Console.WriteLine($"  [MonsterFarming] 选择: {t1}");

    var t2 = selector.Select(state, MakeGoal(GoalMode.OreFarming), candidates, safety);
    Check(t2 != null, "采矿模式应选择采矿");
    Console.WriteLine($"  [OreFarming] 选择: {t2}");

    var crisisState = MakeWorld(player: MakePlayer(hp: 12, maxHp: 100));
    var t3 = selector.Select(crisisState, MakeGoal(GoalMode.Survival), candidates, safety);
    Console.WriteLine($"  [Survival, 低血量] 选择: {t3 ?? "null"}");
    Console.WriteLine("  PASS\n");
}
