using Microsoft.Maui.Graphics;
using Orbit.Engine;

namespace ProseForge.Game;

public sealed class GuildHallScene : GameScene
{
    private double elapsedMilliseconds;

    public static bool IsWorkbenchHit(PointF point, float width, float height)
    {
        var centerX = width / 2;
        var workbenchY = height / 2 - 5;
        return point.X >= centerX - 86 && point.X <= centerX + 86
            && point.Y >= workbenchY - 10 && point.Y <= workbenchY + 54;
    }

    public override void Update(double millisecondsSinceLastUpdate) => elapsedMilliseconds += millisecondsSinceLastUpdate;

    public override void Render(ICanvas canvas, RectF dimensions)
    {
        base.Render(canvas, dimensions);
        canvas.FillColor = Color.FromArgb("#241C26");
        canvas.FillRectangle(dimensions);

        var pulse = (float)(Math.Sin(elapsedMilliseconds / 420) * 4);
        var centerX = dimensions.Center.X;
        var workbenchY = dimensions.Center.Y - 5;

        canvas.FillColor = Color.FromArgb("#8B5633");
        canvas.FillRoundedRectangle(centerX - 82, workbenchY + 28, 164, 22, 7);
        canvas.FillColor = Color.FromArgb("#C48A55");
        canvas.FillRoundedRectangle(centerX - 72, workbenchY + pulse, 144, 32, 8);
        canvas.FillColor = Color.FromArgb("#F6D68F");
        canvas.FillCircle(centerX, workbenchY + 10 + pulse, 9);

        canvas.FontColor = Color.FromArgb("#FFF4E2");
        canvas.FontSize = 20;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString("Manuscript desk", 14, 18, dimensions.Width - 28, 30, HorizontalAlignment.Center, VerticalAlignment.Center);

    }
}
