using System;
using System.Collections.Generic;
using System.Text.Json;
using BananaTime.Physics;
using BenMakesGames.PlayPlayMini;
using BenMakesGames.PlayPlayMini.GraphicsExtensions;
using BenMakesGames.PlayPlayMini.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace BananaTime.GameStates;

public sealed record LevelEditorConfig(string PictureName, IReadOnlyList<IReadOnlyList<Vector2>>? InitialShapes = null);

public sealed class LevelEditor : GameState<LevelEditorConfig>
{
    private const int PointRadius = 3;
    private const int PointHitRadius = 5;
    private const float PanSpeed = 240f;

    private static readonly Keys[] PanLeftKeys = { Keys.A, Keys.Left, Keys.NumPad4 };
    private static readonly Keys[] PanRightKeys = { Keys.D, Keys.Right, Keys.NumPad6 };
    private static readonly Keys[] PanUpKeys = { Keys.W, Keys.Up, Keys.NumPad8 };
    private static readonly Keys[] PanDownKeys = { Keys.S, Keys.Down, Keys.NumPad2 };

    private GraphicsManager Graphics { get; }
    private GameStateManager GSM { get; }
    private MouseManager Mouse { get; }
    private KeyboardManager Keyboard { get; }

    private readonly string PictureName;
    private readonly List<EditorShape> Shapes = new();
    private int? EditingShape;
    private Vector2 PanOffset;

    private sealed class EditorShape
    {
        public readonly List<Vector2> Points = new();
        public bool IsConvexClockwise = true;

        public void Add(Vector2 p) { Points.Add(p); Recompute(); }
        public void PopLast() { Points.RemoveAt(Points.Count - 1); Recompute(); }

        private void Recompute()
        {
            if (Points.Count < 3) { IsConvexClockwise = true; return; }
            int n = Points.Count;
            int sign = 0;
            for (int i = 0; i < n; i++)
            {
                var a = Points[i];
                var b = Points[(i + 1) % n];
                var c = Points[(i + 2) % n];
                float cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (cross == 0) continue;
                int s = cross > 0 ? 1 : -1;
                if (sign == 0) sign = s;
                else if (sign != s) { IsConvexClockwise = false; return; }
            }
            IsConvexClockwise = sign >= 0;
        }
    }

    public LevelEditor(LevelEditorConfig config, GraphicsManager graphics, GameStateManager gsm, MouseManager mouse, KeyboardManager keyboard)
    {
        PictureName = config.PictureName;
        Graphics = graphics;
        GSM = gsm;
        Mouse = mouse;
        Keyboard = keyboard;

        if (config.InitialShapes != null)
        {
            foreach (var pts in config.InitialShapes)
            {
                var s = new EditorShape();
                foreach (var p in pts) s.Add(p);
                if (s.Points.Count > 0) Shapes.Add(s);
            }
        }
    }

    public override void Enter() => Mouse.UseSystemCursor();
    public override void Leave() => Mouse.UseNoCursor();

    public override void Input(GameTime gameTime)
    {
        if (Keyboard.PressedKey(Keys.Escape))
        {
            if (EditingShape.HasValue)
                EditingShape = null;
            else
                GSM.ChangeState<LevelPicker>();
            return;
        }

        if (Keyboard.PressedKey(Keys.X))
        {
            ExportToConsole();
            return;
        }

        if (!Mouse.IsInWindow()) return;

        var mousePic = ScreenToPicture(new Vector2(Mouse.X, Mouse.Y));

        if (Mouse.LeftClicked)
        {
            int? hit = FindShapeAtPoint(mousePic);
            if (hit.HasValue)
            {
                EditingShape = hit;
            }
            else if (EditingShape.HasValue)
            {
                Shapes[EditingShape.Value].Add(mousePic);
            }
            else
            {
                var s = new EditorShape();
                s.Add(mousePic);
                Shapes.Add(s);
                EditingShape = Shapes.Count - 1;
            }
        }
        else if (Mouse.RightClicked && EditingShape.HasValue)
        {
            var shape = Shapes[EditingShape.Value];
            shape.PopLast();
            if (shape.Points.Count == 0)
            {
                Shapes.RemoveAt(EditingShape.Value);
                EditingShape = null;
            }
        }
    }

    public override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        float dx = 0f, dy = 0f;
        if (Keyboard.AnyKeyDown(PanLeftKeys)) dx -= 1f;
        if (Keyboard.AnyKeyDown(PanRightKeys)) dx += 1f;
        if (Keyboard.AnyKeyDown(PanUpKeys)) dy -= 1f;
        if (Keyboard.AnyKeyDown(PanDownKeys)) dy += 1f;
        if (dx != 0f || dy != 0f)
            PanOffset += new Vector2(dx, dy) * PanSpeed * dt;
    }

    private Vector2 ScreenToPicture(Vector2 screen) => screen + PanOffset;
    private Vector2 PictureToScreen(Vector2 pic) => pic - PanOffset;

    private int? FindShapeAtPoint(Vector2 pic)
    {
        int rSq = PointHitRadius * PointHitRadius;
        for (int i = 0; i < Shapes.Count; i++)
        {
            foreach (var p in Shapes[i].Points)
            {
                if (Vector2.DistanceSquared(p, pic) <= rSq)
                    return i;
            }
        }
        return null;
    }

    public override void Draw(GameTime gameTime)
    {
        Graphics.Clear(new Color(20, 30, 40));

        if (Graphics.Pictures.ContainsKey(PictureName))
            Graphics.DrawPicture(PictureName, -(int)PanOffset.X, -(int)PanOffset.Y);

        DrawShapes();
        DrawHud();
    }

    private void DrawShapes()
    {
        for (int i = 0; i < Shapes.Count; i++)
        {
            var shape = Shapes[i];
            var pts = shape.Points;
            if (pts.Count == 0) continue;

            var color = shape.IsConvexClockwise ? Color.White : Color.Red;
            var implied = color * 0.5f;

            for (int j = 0; j < pts.Count - 1; j++)
                DrawLine(PictureToScreen(pts[j]), PictureToScreen(pts[j + 1]), color);

            if (pts.Count >= 2)
                DrawLine(PictureToScreen(pts[^1]), PictureToScreen(pts[0]), implied);

            foreach (var p in pts)
            {
                var s = PictureToScreen(p);
                Graphics.DrawFilledCircle((int)s.X, (int)s.Y, PointRadius, color);
            }
        }
    }

    private void DrawLine(Vector2 a, Vector2 b, Color color)
    {
        var diff = b - a;
        float len = diff.Length();
        if (len <= 0f) return;
        float angle = (float)System.Math.Atan2(diff.Y, diff.X);
        Graphics.SpriteBatch.Draw(
            Graphics.WhitePixel,
            a,
            null,
            color,
            angle,
            new Vector2(0f, 0.5f),
            new Vector2(len, 1f),
            SpriteEffects.None,
            0f
        );
    }

    private void ExportToConsole()
    {
        var editorPolygons = new List<object>(Shapes.Count);
        var polygons = new List<object>(Shapes.Count);

        foreach (var shape in Shapes)
        {
            var ptsPx = new List<object>(shape.Points.Count);
            var ptsM = new List<object>(shape.Points.Count);
            foreach (var p in shape.Points)
            {
                ptsPx.Add(new { x = p.X, y = p.Y });
                ptsM.Add(new { x = p.X * PhysicsConstants.MetersPerPixel, y = p.Y * PhysicsConstants.MetersPerPixel });
            }
            editorPolygons.Add(new { points = ptsPx, isConvexClockwise = shape.IsConvexClockwise });
            polygons.Add(ptsM);
        }

        var payload = new { picture = PictureName, editorPolygons, polygons };
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void DrawHud()
    {
        Graphics.DrawText("Font", 4, 4, $"editing: {PictureName}", Color.Yellow);
        var modeText = EditingShape.HasValue ? $"shape {EditingShape.Value} ({Shapes[EditingShape.Value].Points.Count} pts)" : "no shape";
        Graphics.DrawText("Font", 4, 14, $"mode: {modeText}", Color.LightGray);
        Graphics.DrawText("Font", 4, 24, $"pan: {(int)PanOffset.X},{(int)PanOffset.Y}", Color.LightGray);
        Graphics.DrawText("Font", 4, Graphics.Height - 12, "WASD/arrows pan  L-click add/select  R-click undo  X export  Esc exit", Color.LightGray);
    }
}
