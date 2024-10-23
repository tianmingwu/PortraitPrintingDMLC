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
                    arr[j,i] = 255 - outputBmp.GetPixel(i, j).R;  // notice the change in row/column for height/width; Bitmap.GetPixel method
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

        public List<double> LeadingLeafMu = new List<double>{}; // MU meter set for leading leaf
        public List<double> TrailingLeafMu = new List<double>{}; // MU meter set for trailing leaf

        public double TotalMu = 0.0;
        public int TotalLength = 0;
        // constructor
        public MlcSequencer(List<double> intensityProfile)
        {
            // From Eq(6) in L.Ma et al paper: intensity profile should be patched with 0
            // add leading zero
            intensityProfile.Insert(0, 0);
            // add trailing zero
            intensityProfile.Add(0);

            // add MU trackers
            double leadingMu = 0;
            double trailingMu = 0;

            //
            for (int i=intensityProfile.Count-2; i>-1; i--)
            {
                var val = intensityProfile.ElementAt(i+1) - intensityProfile.ElementAt(i);
                if (val > 0)
                {
                    PositiveCoefficients.Add(val);
                    LeadingLeafPositions.Add(i);
                    leadingMu += val;
                    LeadingLeafMu.Add(leadingMu);
                }
                else if (val < 0)
                {
                    NegativeCoefficients.Add(val);
                    TrailingLeafPositions.Add(i);
                    trailingMu -= val;
                    TrailingLeafMu.Add(trailingMu);
                }

            }

            TotalMu = PositiveCoefficients.Sum();
            TotalLength = PositiveCoefficients.Count;

        }
    }

    public struct Trajectory
    {
        public double MeterSet {get; }
        public double LeadingLeafPos {get; set;}
        public double TrailingLeafPos {get; set;}    

        public Trajectory(double meter, double leading, double trailing)
        {
            MeterSet = meter;
            LeadingLeafPos = leading;
            TrailingLeafPos = trailing;
        }

        public override string ToString() => $"MeterSet, leading, trailing: {MeterSet}, {LeadingLeafPos}, {TrailingLeafPos}";


    }

    public List<Trajectory> WriteTrajectoryTable(double deltaMu, MlcSequencer pair, double restPosition, double leafPosIncrement = 0.1)
    {
        List<Trajectory> table = new List<Trajectory>();
        
        double meterSet = 0;

        double leadingLeafPosition = restPosition;
        double trailingLeafPosition = restPosition;

        int lead = 0;
        int trail = 0;

        while (meterSet + deltaMu < 1)
        {
            Console.WriteLine($"{pair.TotalLength}, {lead}, {trail}");

            if (lead < pair.TotalLength) // not done yet
            {
                if (pair.LeadingLeafMu.ElementAt(lead)/pair.TotalMu > meterSet)// update leading Leaf Position
                    {
                        leadingLeafPosition += leafPosIncrement * pair.LeadingLeafPositions.ElementAt(lead);
                        lead++;
                    }
            }
            else
            {
                // done; stay put
            }

            if (trail < pair.TotalLength) // not done yet
            {
                if (pair.TrailingLeafMu.ElementAt(trail)/pair.TotalMu > meterSet) // update trailing leaf Position
                {
                    trailingLeafPosition += leafPosIncrement * pair.TrailingLeafPositions.ElementAt(trail);
                    trail++;
                }
            }
            else
            {
                // done; close trailing leaf
                trailingLeafPosition = leadingLeafPosition;
            }
            
            table.Add(new Trajectory(meterSet, leadingLeafPosition, trailingLeafPosition));
            meterSet += deltaMu;
        }
        return table;
    }