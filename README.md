# Introduction

We aim to develop a computer program to write an MLC file that is executable by Varian Linear accelerators to print an arbitrary photo in jpg or bmp format, e.g., a portrait of a public figure.

# Materials and Methods

## Image Digitization

The best aspect ratio of a portrait is usually 3:2. For simplicity, we decide to use a single field to print the portrait. Since it is 14.4cm width span in the leaf traveling direction, the perpendicular length of the portrait shall be approximately 21cm.

For Varian M120 MLC has 60 leaf pairs; presumably travel resolution is 0.05cm; Therefore we aiming for re-digitize the original image into a $60\times280$ array with a gray scale value from 0 t0 255 (white to black). In that array, the central portion of 21cm, in corresponding to leaf pairs 10-51, will be used for the image while the others will be patched by zero.

During irradiation, the suggested collimator settings are

```c#
X1 = 7.0, 
X2 = 7.0,
Y1 = 10.5,
Y2 = 10.5
```

and the film should be placed at Linac isocenter.

```c#
# Create a BMP from image file
Bitmap bmp = new Bitmap(fileName)

# Make a colored image into gray scale
Color pixelColor = bmp.GetPixel(i, j);
byte gray = (byte) (0.21 * pixelColor.R + 0.72 * pixelColor.G + 0.07*pixelColor.B);
bmp.SetPixel(i, j, Color.FromArgb(gray, gray, gray));
```

## MLC Sequencing Algorithm

We implemented [the algorithm](https://iopscience.iop.org/article/10.1088/0031-9155/43/6/019/meta) described by *Ma et al*[[1]](#1).

# References

<a id="1">[1]</a> Ma L. *et al* (1998) *Phys. Med. Biol.* **43** 1629
