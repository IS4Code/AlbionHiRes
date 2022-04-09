## Rendering HD upscaled Albion 2D textures

**Disclaimer: This is not a simple process, and it may take a significant amount of your time or computer's resources (depending on the capabilities).**

### Requirements

* For the extraction: C# support and .NET Framework. The code is using [AlbLib](https://github.com/IllidanS4/AlbLib).
* For the upscaling: [ESRGAN](https://github.com/xinntao/ESRGAN) (in Python) and the [deviantPixelHD_250000.pth](https://drive.google.com/file/d/114yFJKeYCcr6st7aNNo9FJ8wDbEzLpdz/view) model (but you can try other models).
* For denoising: [waifu2x-caffe](https://github.com/lltcggie/waifu2x-caffe/) or similar.
* For recombining the tiles: ImageMagick (and Batch).
* For using in the game: [UAlbion](https://github.com/csinkers/ualbion).

### Steps

All the scripts should be executed from the ESRGAN root directory.

#### Extracting the tiles (with context)

The C# code uses AlbLib to go through all the game's tiles, and tries to find them in existing maps. This is a requirement since otherwise the neural network will not have enough information to determine the nature of the image and may introduce artifacts.

To use `extract.csx`, all you need is to set-up AlbLib and execute the main method:
```cs
Paths.SetGameDir(/* directory where the game resides */);
ExportTileContexts(/* directory where to put the results */);
```
The process will create the `LR` directory and `masknames.bat` file. The directory shall contain the exported tiles alongside their masks, and the Batch script will be used to recombine them after upscaling.

#### Pre-upscaling polishing

If you want, you can now go through the exported files and try to look for any mistakes or do other modifications before the upscaling. For example, some tiles may have partially missing context, or have the wrong background. This may be automated to some degree, but it will not be perfect.

As a suggestion, you can use some heuristics to determine whether a particular overlay tile should continue to the neighboring tiles or not, based on the count of non-transparent pixel alongside its edge in that direction.

Additionally, the masks may need some blurring, otherwise the upscaled masks may have too sharp edges (but still mostly fine).

#### Upscaling the tiles

Now that the results are in the `LR` directory, it is time to start upscaling.

First, create the `results` directory and directories within to match the structure of `LR` (you can use the provided `dirs.bat` script).

Then, use the provided `test.py` file to replace one in the original ESRGAN repository, then run it specifying the path to the deviantPixelHD model, like this:
```bat
python test.py models/deviantPixelHD_250000.pth
```
This process took me about 8 hours (even on GPU), so be prepared for that.

#### Post-upscaling polishing

It seems deviantPixelHD was trained, at least partially, on JPEG-compressed files, and you may noticed some artifacts in the results. You may use waifu2x to denoise all the results, with the following settings:

* Denoise level: Level 1
* Model: 2-D illust (CUnet Model)
* Split size: 128
* Batch size: 1

Also the masks will not be grayscale anymore after upscaling. You could perform some additional blurring, grayscaling etc. here.

You should end up with a `results(CUnet)(noise)(Level1)` directory.

#### Recombining the tiles

All you need now is to create the `final` directory and run `masknames.bat`. During the process, ImageMagick is invoked for every tile to create the result, with these steps:

* The original tile (from the `LR` directory) is loaded, resizes, and overlaid on top of the upscaled one, using the "colorize" transform. This step is optional, but it fixes the toning of the upscaled images (since the model may sometimes change the color a little).
* If there is a mask, it is used to re-add transparency to the image. Note that if you use the tile as an underlay in the future, you may want to remove the alpha channel altogether, to access the original color.
* The image is cropped to a center 64x64 square.

In most cases, neighboring tiles will look fine when placed alongside each other in the game, but there may be places where the edge could be visible. In that case, you can decide not to perfectly crop the images and instead perform some averaging along the edges when the tiles are displayed together.