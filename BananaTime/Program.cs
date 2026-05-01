using BenMakesGames.PlayPlayMini;
using BenMakesGames.PlayPlayMini.Model;
using BananaTime;
using BananaTime.GameStates;
using Serilog.Extensions.Autofac.DependencyInjection;
using Serilog;

/*if (!SteamHelpers.Startup())
    return;*/

DirectoryHelpers.EnsureDirectoryExists();

var gsmBuilder = new GameStateManagerBuilder();

gsmBuilder
    .SetWindowSize(1920 / 3, 1080 / 3, 2)
    .SetInitialGameState<Startup>()

    // TODO: set a better window title
    .SetWindowTitle("Banana Time")

    // TODO: add any resources needed (refer to PlayPlayMini documentation for more info)
    .AddAssets([
        new FontMeta("Font", "Graphics/Font", 6, 8, verticalSpacing: 1, horizontalSpacing: 0),
        new PictureMeta("Banana", "Graphics/Banana"),

        new PictureMeta("StoneHenge", "Graphics/StoneHenge"),

        // new FontMeta(...)
        // new PictureMeta(...)
        // new SpriteSheetMeta(...)
        // new SongMeta(...)
        // new SoundEffectMeta(...)
    ])

    // TODO: any additional service registration (refer to PlayPlayMini and/or Autofac documentation for more info)
    .AddServices((s, c) => {
        var loggerConfig = new LoggerConfiguration()
            .WriteTo.File(Path.Join(DirectoryHelpers.LogDirectory, "Log.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
        ;

        s.RegisterSerilog(loggerConfig);
    })
;

gsmBuilder.Run();

Log.Information("Shutting down - thanks for playing! :)");

//SteamHelpers.Shutdown();
