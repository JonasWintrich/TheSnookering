using System.Text.Json;
using Snookering.Core.Tables;

// CLI utilities around the pure Core library.
//   dotnet run --project src/Snookering.Tools -- dump-tables <out.json>
// Emits both table specs as JSON — the single source the Blender hero-table
// script reads, so visual geometry can never drift from the physics.

if (args.Length >= 1 && args[0] == "dump-tables")
{
    var outPath = args.Length >= 2 ? args[1] : "tables.json";

    object Dump(TableSpec t) => new
    {
        name = t.Name,
        halfLength = t.HalfLength,
        halfWidth = t.HalfWidth,
        ballRadius = t.Physics.R,
        cushions = t.Cushions.Select(c => new
        {
            ax = c.A.X, ay = c.A.Y, bx = c.B.X, by = c.B.Y, nx = c.N.X, ny = c.N.Y,
        }),
        jaws = t.Jaws.Select(j => new
        {
            cx = j.Center.X, cy = j.Center.Y, r = j.Radius,
            sx = j.StartDir.X, sy = j.StartDir.Y, ex = j.EndDir.X, ey = j.EndDir.Y,
        }),
        pockets = t.Pockets.Select(p => new
        {
            x = p.FallCenter.X, y = p.FallCenter.Y, r = p.FallRadius, id = p.Id,
        }),
        snooker = t.Snooker is null ? null : new
        {
            baulkX = t.Snooker.BaulkX,
            dRadius = t.Snooker.DRadiusValue,
            spots = new[]
            {
                new { id = (int)SnookerBalls.Yellow, x = t.Snooker.Yellow.X, y = t.Snooker.Yellow.Y },
                new { id = (int)SnookerBalls.Green, x = t.Snooker.Green.X, y = t.Snooker.Green.Y },
                new { id = (int)SnookerBalls.Brown, x = t.Snooker.Brown.X, y = t.Snooker.Brown.Y },
                new { id = (int)SnookerBalls.Blue, x = t.Snooker.Blue.X, y = t.Snooker.Blue.Y },
                new { id = (int)SnookerBalls.Pink, x = t.Snooker.Pink.X, y = t.Snooker.Pink.Y },
                new { id = (int)SnookerBalls.Black, x = t.Snooker.Black.X, y = t.Snooker.Black.Y },
            },
        },
    };

    var json = JsonSerializer.Serialize(new
    {
        pool = Dump(TableSpec.Pool9ft()),
        snooker = Dump(TableSpec.Snooker12ft()),
    }, new JsonSerializerOptions { WriteIndented = true });

    File.WriteAllText(outPath, json);
    Console.WriteLine($"wrote {Path.GetFullPath(outPath)}");
    return 0;
}

Console.Error.WriteLine("usage: dump-tables <out.json>");
return 1;
