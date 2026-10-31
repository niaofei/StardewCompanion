namespace StardewMiningAgent.Models;

public enum MineKind { Normal, SkullCavern, Volcano, Custom }

public enum GoalMode { MonsterFarming, OreFarming, SkullCavernRush, Exploration, Survival }

public enum TaskState { Pending, Running, Completed, Failed, Aborted }

public enum Direction { Up = 0, Right = 1, Down = 2, Left = 3, UpRight = 4, UpLeft = 5, DownRight = 6, DownLeft = 7 }

public enum TileKind : byte
{
    Empty = 0,
    Stone = 1,
    Copper = 2,
    Iron = 3,
    Gold = 4,
    Iridium = 5,
    Special = 6,
    Crate = 7,
    LadderDown = 8,
    MineEntrance = 9,
    Wall = 10,
    Water = 11,
    Tree = 12
}

public enum WeaponKind { Sword, Dagger, Club }

public enum EnemyBehavior { Tracking, Hopping, Fly, Ghostly, Charging, Projectile }

public enum RiskLevel { Safe, Low, Medium, High, Critical }
