using MazeWars.GameServer.Models;

namespace MazeWars.GameServer.Utils;

public static class GameMathUtils
{
    public static float Distance(Vector2 a, Vector2 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector2(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t
        );
    }

    public static float AngleBetween(Vector2 a, Vector2 b)
    {
        return (float)Math.Atan2(b.Y - a.Y, b.X - a.X);
    }

    public static Vector2 RotateVector(Vector2 vector, float angle)
    {
        var cos = (float)Math.Cos(angle);
        var sin = (float)Math.Sin(angle);

        return new Vector2(
            vector.X * cos - vector.Y * sin,
            vector.X * sin + vector.Y * cos
        );
    }

    public static bool IsInRange(Vector2 position, Vector2 target, float range)
    {
        return Distance(position, target) <= range;
    }

    public static Vector2 ClampToWorldBounds(Vector2 position, float worldSize)
    {
        return new Vector2(
            Math.Clamp(position.X, -worldSize, worldSize),
            Math.Clamp(position.Y, -worldSize, worldSize)
        );
    }

    public static int CalculateExperienceRequired(int level)
    {
        return level * 1000 + (level - 1) * 500;
    }

    public static float CalculateDamageReduction(int armor)
    {
        return armor / (armor + 100f);
    }

    public static int CalculateCriticalDamage(int baseDamage, float critMultiplier = 1.5f)
    {
        return (int)(baseDamage * critMultiplier);
    }
}
