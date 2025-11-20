using MessagePack;

namespace MazeWars.GameServer.Models;

[MessagePackObject]
public struct Vector2
{
    [Key(0)]
    public float X { get; set; }

    [Key(1)]
    public float Y { get; set; }

    [IgnoreMember]
    public static Vector2 Zero => new(0, 0);

    [IgnoreMember]
    public static Vector2 One => new(1, 1);

    [IgnoreMember]
    public static Vector2 UnitX => new(1, 0);

    [IgnoreMember]
    public static Vector2 UnitY => new(0, 1);


    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }


    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator *(Vector2 a, float scalar) => new(a.X * scalar, a.Y * scalar);
    public static Vector2 operator *(float scalar, Vector2 a) => new(a.X * scalar, a.Y * scalar);
    public static Vector2 operator /(Vector2 a, float scalar) => new(a.X / scalar, a.Y / scalar);
    public static Vector2 operator -(Vector2 a) => new(-a.X, -a.Y);


    public static bool operator ==(Vector2 a, Vector2 b) => Math.Abs(a.X - b.X) < float.Epsilon && Math.Abs(a.Y - b.Y) < float.Epsilon;
    public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

    [IgnoreMember]
    public readonly float Magnitude => (float)Math.Sqrt(X * X + Y * Y);

    [IgnoreMember]
    public readonly float SqrMagnitude => X * X + Y * Y; 


    public readonly Vector2 GetNormalized()
    {
        var mag = Magnitude;
        return mag > 0 ? new Vector2(X / mag, Y / mag) : Zero;
    }

    public void Normalize()
    {
        var mag = Magnitude;
        if (mag > 0)
        {
            X /= mag;
            Y /= mag;
        }
        else
        {
            X = 0;
            Y = 0;
        }
    }


    public static float Distance(Vector2 a, Vector2 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    public static float SqrDistance(Vector2 a, Vector2 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return a + (b - a) * t;
    }

    public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t)
    {
        return a + (b - a) * t;
    }

    public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistanceDelta)
    {
        var direction = target - current;
        var distance = direction.Magnitude;

        if (distance <= maxDistanceDelta || distance < float.Epsilon)
            return target;

        return current + direction.GetNormalized() * maxDistanceDelta;
    }

    public static Vector2 ClampMagnitude(Vector2 vector, float maxLength)
    {
        if (vector.SqrMagnitude > maxLength * maxLength)
            return vector.GetNormalized() * maxLength;
        return vector;
    }

    public static float Dot(Vector2 a, Vector2 b)
    {
        return a.X * b.X + a.Y * b.Y;
    }

    public static Vector2 Reflect(Vector2 inDirection, Vector2 inNormal)
    {
        return inDirection - 2f * Dot(inDirection, inNormal) * inNormal;
    }

    public static Vector2 Perpendicular(Vector2 inDirection)
    {
        return new Vector2(-inDirection.Y, inDirection.X);
    }

    public static float Angle(Vector2 from, Vector2 to)
    {
        var denominator = (float)Math.Sqrt(from.SqrMagnitude * to.SqrMagnitude);
        if (denominator < float.Epsilon)
            return 0f;

        var dot = Math.Clamp(Dot(from, to) / denominator, -1f, 1f);
        return (float)Math.Acos(dot) * (180f / (float)Math.PI);
    }

    public static float SignedAngle(Vector2 from, Vector2 to)
    {
        var unsignedAngle = Angle(from, to);
        var sign = Math.Sign(from.X * to.Y - from.Y * to.X);
        return unsignedAngle * sign;
    }

    public static Vector2 Min(Vector2 a, Vector2 b)
    {
        return new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
    }

    public static Vector2 Max(Vector2 a, Vector2 b)
    {
        return new Vector2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    // =============================================
    // OBJECT OVERRIDES
    // =============================================

    public override bool Equals(object? obj)
    {
        return obj is Vector2 vector && this == vector;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    public override string ToString()
    {
        return $"({X:F2}, {Y:F2})";
    }

    public string ToString(string format)
    {
        return $"({X.ToString(format)}, {Y.ToString(format)})";
    }

    // =============================================
    // UTILITY METHODS
    // =============================================

    [IgnoreMember]
    public readonly bool IsNormalized => Math.Abs(SqrMagnitude - 1f) < 0.01f;

    [IgnoreMember]
    public readonly bool IsZero => X == 0f && Y == 0f;

    public readonly Vector2 Abs() => new Vector2(Math.Abs(X), Math.Abs(Y));

    public readonly Vector2 Round() => new Vector2((float)Math.Round(X), (float)Math.Round(Y));

    public readonly Vector2 Floor() => new Vector2((float)Math.Floor(X), (float)Math.Floor(Y));

    public readonly Vector2 Ceiling() => new Vector2((float)Math.Ceiling(X), (float)Math.Ceiling(Y));

    // =============================================
    // CONVERSION METHODS
    // =============================================

    public readonly (float x, float y) ToTuple() => (X, Y);

    public readonly float[] ToArray() => new float[] { X, Y };

    // =============================================
    // IMPLICIT CONVERSIONS (opcional)
    // =============================================

    public static implicit operator Vector2((float x, float y) tuple) => new(tuple.x, tuple.y);

    // =============================================
    // EXTENSIONS ESPECÍFICAS PARA EL JUEGO
    // =============================================

    /// <summary>
    /// Rota el vector por un ángulo en radianes
    /// </summary>
    public readonly Vector2 Rotate(float angleRadians)
    {
        var cos = (float)Math.Cos(angleRadians);
        var sin = (float)Math.Sin(angleRadians);
        return new Vector2(
            X * cos - Y * sin,
            X * sin + Y * cos
        );
    }

    /// <summary>
    /// Rota el vector por un ángulo en grados
    /// </summary>
    public readonly Vector2 RotateDegrees(float angleDegrees)
    {
        return Rotate(angleDegrees * (float)Math.PI / 180f);
    }

    /// <summary>
    /// Obtiene el ángulo del vector en radianes
    /// </summary>
    public readonly float ToAngle()
    {
        return (float)Math.Atan2(Y, X);
    }

    /// <summary>
    /// Obtiene el ángulo del vector en grados
    /// </summary>
    public readonly float ToAngleDegrees()
    {
        return ToAngle() * 180f / (float)Math.PI;
    }

    /// <summary>
    /// Crea un vector desde un ángulo y magnitud
    /// </summary>
    public static Vector2 FromAngle(float angleRadians, float magnitude = 1f)
    {
        return new Vector2(
            (float)Math.Cos(angleRadians) * magnitude,
            (float)Math.Sin(angleRadians) * magnitude
        );
    }

    /// <summary>
    /// Crea un vector desde un ángulo en grados y magnitud
    /// </summary>
    public static Vector2 FromAngleDegrees(float angleDegrees, float magnitude = 1f)
    {
        return FromAngle(angleDegrees * (float)Math.PI / 180f, magnitude);
    }
}