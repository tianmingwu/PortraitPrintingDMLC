    using System.Drawing;
    using System.Linq;

    public class ImageDigitizer
    {
        public static double[,] Digitize(string fileName, int targetHeight=42, int targetWidth=140)
        {
            Bitmap bmp = new Bitmap(fileName);

            double[,] arr = new double[targetHeight, targetWidth];

            Bitmap outputBmp = new Bitmap(targetWidth, targetHeight);

            // change original BMP to gray scale
            for (int i=0;i<bmp.Width;i++)
            {
                for (int j=0;j<bmp.Height;j++)
                {
                    Color pixelColor = bmp.GetPixel(i, j);
                    byte gray = (byte) (0.21 * pixelColor.R + 0.72 * pixelColor.G + 0.07 * pixelColor.B);
                    Color newColor = Color.FromArgb(gray, gray, gray);
                    bmp.SetPixel(i, j, newColor);
                }
            }

            // adjust resolution
            outputBmp.SetResolution(
                bmp.HorizontalResolution * targetWidth / bmp.Width,
                bmp.VerticalResolution * targetHeight /  bmp.Height
            );

            // redraw the original bmp to the target size
            using (Graphics g = Graphics.FromImage(outputBmp))
            {
                g.DrawImage(bmp, new Rectangle(0, 0, targetWidth, targetHeight));
            }

            // extract array data from the resampled bmp; 
            for (int j=0;j<outputBmp.Height;j++)            // loop over image rows
            {
                for (int i=0;i<outputBmp.Width;i++)         // loop over image columns
                {
                    arr[j,i] = outputBmp.GetPixel(i, j).R;  // notice the change in row/column for height/width; Bitmap.GetPixel method
                }
            }
            
            outputBmp.Save("./images/rw_grayscale_resampled.bmp");
            Console.WriteLine($"new image size: {outputBmp.Width} x {outputBmp.Height}");
            Console.WriteLine($"new image resolution: {outputBmp.HorizontalResolution}, {outputBmp.VerticalResolution}");

            return arr;
        }
    }

    public class MlcSequencer
    {
        // in this implementation, leaf is moving from right to left
        public List<double> TrailingLeafPositions = new List<double>{};
        public List<double> LeadingLeafPositions = new List<double>{};

        public List<double> PositiveCoefficients = new List<double>{}; // incremental changes in intensity from one point along the profile to the next; positive
        public List<double> NegativeCoefficients = new List<double>{}; // incremental changes in intensity from one point along the profile to the next; negative

        // constructor
        public MlcSequencer(List<double> intensityProfile)
        {
            // add leading zero
            intensityProfile.Insert(0, 0);

            //
            for (int i=intensityProfile.Count-2; i>-1; i--)
            {
                var val = intensityProfile.ElementAt(i+1) - intensityProfile.ElementAt(i);
                if (val > 0)
                {
                    PositiveCoefficients.Add(val);
                    LeadingLeafPositions.Add(i);
                }
                else if (val < 0)
                {
                    NegativeCoefficients.Add(val);
                    TrailingLeafPositions.Add(i);
                }

            }

        }
    }