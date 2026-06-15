# <img src="data:image/svg+xml;utf8,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 512 512'%3E%3Cpath fill='%236c5ce7' d='M256 0C114.6 0 0 114.6 0 256s114.6 256 256 256 256-114.6 256-256S397.4 0 256 0zm0 464c-114.7 0-208-93.3-208-208S141.3 48 256 48s208 93.3 208 208-93.3 208-208 208zm88-312c-26.5 0-48 21.5-48 48v8c0 22.1-17.9 40-40 40s-40-17.9-40-40v-8c0-26.5-21.5-48-48-48s-48 21.5-48 48v8c0 48.6 39.4 88 88 88v32c0 13.3 10.7 24 24 24s24-10.7 24-24v-32c48.6 0 88-39.4 88-88v-8c0-26.5-21.5-48-48-48z'/%3E%3C/svg%3E" alt="NeoPaula Logo" width="48" height="48" style="vertical-align: bottom;"> NeoPaula

![NuGet Version](https://img.shields.io/nuget/v/NeoPaula.svg)
![Build Status](https://img.shields.io/github/actions/workflow/status/your-user/NeoPaula/dotnet.yml)

**NeoPaula** is a modern C# library for playing old school Amiga Protracker / Noisetracker `.mod` music files as well as OctaMED `.mmd` files.

## Features

- Uses the robust [NAudio](https://github.com/naudio/NAudio) library for sound playback.
- Returns basic metainformation about a track without playing (e.g. format, title, channels).
- Seamlessly plays from either a `Stream` or a `filename`.
- Autodetects tracker format from magics directly in streams.

## Getting Started

### Indexing a Folder of Tracks

You can fetch track info (Title, Channels, Format) without playing the entire file, perfect for building music libraries or indices.

```csharp
using System.IO;
using NeoPaula;

var player = new NeoPaulaPlayer();

foreach (var file in Directory.GetFiles("./music", "*.*"))
{
    var info = player.GetTrackInfo(file);
    Console.WriteLine($"Found {info.Format} track: {info.Title} with {info.Channels} channels");
}
```

### Playback from Filename or Stream

```csharp
using NeoPaula;

using (var player = new NeoPaulaPlayer())
{
    // Play directly from a file
    player.Play("my_amiga_tune.mod");

    // Or play from a stream!
    // var stream = File.OpenRead("my_octamed_tune.mmd");
    // player.Play(stream);

    Console.WriteLine("Playing... Press any key to stop.");
    Console.ReadKey();
}
```

## Contributing

Contributions, issues, and feature requests are welcome!

## License

[MIT](LICENSE)
