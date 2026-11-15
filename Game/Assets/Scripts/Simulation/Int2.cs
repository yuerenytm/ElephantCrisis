/// <summary>
/// 中立的格子坐标：两个整数，纯 C#。
///
/// 逻辑层所有坐标都用它，Unity 那边的 Vector2Int / Vector3 只出现在表现层。
/// 这样核心库可以脱离 Unity 编译，也能直接跑在服务器上。
/// </summary>
public readonly struct Int2 : System.IEquatable<Int2>
{
    public readonly int X;
    public readonly int Y;

    public Int2(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static Int2 Zero => new Int2(0, 0);

    public static Int2 Up => new Int2(0, 1);
    public static Int2 Down => new Int2(0, -1);
    public static Int2 Left => new Int2(-1, 0);
    public static Int2 Right => new Int2(1, 0);

    /// <summary>四邻（上下左右）。</summary>
    public static readonly Int2[] Cardinals =
    {
        Up, Right, Down, Left
    };

    public static Int2 operator +(Int2 a, Int2 b) => new Int2(a.X + b.X, a.Y + b.Y);
    public static Int2 operator -(Int2 a, Int2 b) => new Int2(a.X - b.X, a.Y - b.Y);
    public static bool operator ==(Int2 a, Int2 b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Int2 a, Int2 b) => !(a == b);

    public bool Equals(Int2 other) => this == other;
    public override bool Equals(object obj) => obj is Int2 other && this == other;
    public override int GetHashCode() => (X * 397) ^ Y;
    public override string ToString() => $"({X},{Y})";


    // ---- 与 UnityEngine.Vector2Int 对齐的成员（迁移期少改调用点）

    public int x => X;
    public int y => Y;

    public static Int2 zero => Zero;
    public static Int2 one => new Int2(1, 1);
    public static Int2 up => Up;
    public static Int2 down => Down;
    public static Int2 left => Left;
    public static Int2 right => Right;

    public static Int2 operator *(Int2 a, int k) => new Int2(a.X * k, a.Y * k);
    public static Int2 operator *(int k, Int2 a) => new Int2(a.X * k, a.Y * k);


#if UNITY_5_3_OR_NEWER
    // 仅在 Unity 里编译：让表现层可以直接把 Vector2Int 传进来（省掉数百处手工转换）。
    // dotnet 构建时该符号未定义，因此核心库依旧不认识 Unity。

    public static implicit operator Int2(UnityEngine.Vector2Int v) => new Int2(v.x, v.y);

    // 反方向不设隐式：双向隐式会让 Int2 + Vector2Int 这类表达式产生歧义。
    // 需要 Int2 → Vector2Int 时用 View 层的 ToV2() 扩展。
#endif

    /// <summary>曼哈顿距离。</summary>
    public int ManhattanTo(Int2 other) => System.Math.Abs(X - other.X) + System.Math.Abs(Y - other.Y);
}
