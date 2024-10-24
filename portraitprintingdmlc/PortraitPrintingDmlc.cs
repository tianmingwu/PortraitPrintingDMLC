    using System.Drawing;
    using System.Linq;
    using System.IO;
    using System;

    public class ImageDigitizer
    {
        public static double[,] Digitize(string fileName, int targetHeight=42, int targetWidth=280)
        {
            Bitmap bmp = new Bitmap(fileName);

            double[,] arr = new double[targetHeight, targetWidth];

            double[,] patchedArr = new double[60, targetWidth];

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
                    //var pv = 255 - outputBmp.GetPixel(i, j).R;
                    var pv = (int) 100 * (255 - outputBmp.GetPixel(i,j).R)/ 255;
                    arr[j,i] = pv;  // notice the change in row/column for height/width; Bitmap.GetPixel method
                    patchedArr[j+9, i] = pv;
                }
            }
            
            outputBmp.Save("./images/rw_grayscale_resampled.bmp");
            Console.WriteLine($"new image size: {outputBmp.Width} x {outputBmp.Height}");
            Console.WriteLine($"new image resolution: {outputBmp.HorizontalResolution}, {outputBmp.VerticalResolution}");
            // before returning arr, patch 0 (not irradiating)
            
            for (int j=0;j<9;j++)
            {
                for (int i=0;i<outputBmp.Width;i++)
                {
                    patchedArr[j, i] = 0;
                }
            }
            for (int j=outputBmp.Height+9;j<9+outputBmp.Height+9;j++)
            {
                for (int i=0; i<outputBmp.Width;i++)
                {
                    patchedArr[j, i] = 0;
                }
            }

            return patchedArr;
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
        //public int TotalLength = 0;
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

    /// using unit MU increment
    /// also index position
    /// implementation of 
    public static List<Trajectory> CreatePairTrajectoryTable(MlcSequencer pair)
    {
        List<Trajectory> table = new List<Trajectory>();
        //int totalPairMu = (int) pair.TotalMu;
        // if (totalFieldMu < totalPairMu)
        // {
        //     throw new ArgumentException("Invalid Value: Total field MU cannot be less than single pair MU");
        // }
        // handling 0 intensity profiles (no irradiation, MLC pair there will stay rest)
        if (pair.TotalMu == 0)
        {
            for (int mu=0; mu<=pair.TotalMu; mu++)
            {
                table.Add(new Trajectory(mu, 0, 0));
            }
        }
        else
        {
            // initializing
            int mu = 0;
            int leadingIdx = 0; 
            int trailingIdx = 0;
            double leadingPos = 0;
            double trailingPos = 0;

            int leadingPositionLength = pair.LeadingLeafPositions.Count;
            int trailingPositionLength = pair.TrailingLeafPositions.Count;

            while (mu<=pair.TotalMu)
            {   
                if (mu<pair.LeadingLeafMu.ElementAt(leadingIdx))
                {
                    leadingPos = pair.LeadingLeafPositions.ElementAt(leadingIdx);
                }
                else
                {
                    //if not at the end
                    if (leadingIdx < leadingPositionLength-1)
                    {
                        // move
                        leadingPos = pair.LeadingLeafPositions.ElementAt(leadingIdx+1);
                        leadingIdx++;
                    }
                    else
                    {
                        // leading leaf stays at the end
                        leadingPos = pair.LeadingLeafPositions.ElementAt(leadingIdx);
                    }
                     // update index
                }

                if (mu<pair.TrailingLeafMu.ElementAt(trailingIdx))
                {
                    trailingPos = pair.TrailingLeafPositions.ElementAt(trailingIdx);
                }
                else
                {
                    //if not at the end
                    if (trailingIdx < trailingPositionLength-1)
                    {
                        // move
                        trailingPos = pair.TrailingLeafPositions.ElementAt(trailingIdx+1);
                        trailingIdx++;
                    }
                    else
                    {
                        trailingPos = leadingPos; // close trailing leaf at the end
                    }
                    
                }

                table.Add(new Trajectory(mu, leadingPos, trailingPos));
                mu++;
            }
        }
        return table;
        
    }

    public static List<List<Trajectory>> CreateFieldTrajectoryTable(List<MlcSequencer> pairs, int fieldsLimit = 499)
    {
        List<List<Trajectory>> tables = new List<List<Trajectory>>();

        // find out the max field mu for all pairs
        int totalFieldMu = (int) pairs.Select(x=>x.TotalMu).Max();

        Console.WriteLine(totalFieldMu);

        foreach(var pair in pairs)
        {
            List<Trajectory> seq = CreatePairTrajectoryTable(pair);

            if (seq.Count == 1) // static pair
            {
                for (int i=1;i<fieldsLimit;i++)
                {
                    seq.Add(new Trajectory(i, 0, 0));
                }
                tables.Add(seq);
            }

            else if (seq.Count < fieldsLimit) // not enough pair
            {
                Trajectory lastTrajectory = seq.Last();
                for (int i=seq.Count;i<fieldsLimit;i++)
                {
                    seq.Add(new Trajectory(i, lastTrajectory.LeadingLeafPos, lastTrajectory.TrailingLeafPos));
                }
                tables.Add(seq);
            }
            else
            {
                // sampling algorithm?
                List<Trajectory> newSeq = new List<Trajectory>();
                for (int i=0; i<fieldsLimit; i++)
                {
                    int idx = (int) i * (seq.Count / fieldsLimit);
                    newSeq.Add(seq[idx]);
                }
                tables.Add(newSeq);
            }           
        }

        return tables;
    }

    public static List<List<Trajectory>> CreateFieldTrajectoryForImageArray(double[,] arr)
    {
        List<double> row;
        List<MlcSequencer> pairs = new List<MlcSequencer>();
        for(int i=0;i<arr.GetLength(0);i++)
        {
            row = Enumerable.Range(0, arr.GetLength(1)).Select(x=>arr[i, x]).ToList();
            pairs.Add(new MlcSequencer(row));
        }
        return CreateFieldTrajectoryTable(pairs);
    }

    public static StringBuilder CreateMlcFileHeader(
        string lastName, 
        string firstName, 
        string patientId, 
        int numOfFields, 
        string mlcModel="Varian 120M", 
        double tolerance=0.5)
    {
        StringBuilder header = new StringBuilder();
        header.AppendLine("File Rev = J");
        header.AppendLine("Treatment = Dynamic Dose");
        header.AppendLine($"Last Name = {lastName}");
        header.AppendLine($"First Name = {firstName}");
        header.AppendLine($"Patient ID = {patientId}");
        header.AppendLine($"Number of Fields = {numOfFields}");
        header.AppendLine($"Model = {mlcModel}");
        header.AppendLine($"Tolerance = {tolerance}");
        header.AppendLine();

        return header;
    }

    public static void WriteMlcFile(List<List<Trajectory>> trajectories)
    {
        string docPath = Path.Combine("./mlc", "test.mlc");

        // compute the length of 
        int totalMu = trajectories.FirstOrDefault().Count;

        var header = CreateMlcFileHeader("ralph", "w", "007", totalMu);

        //File.WriteAllText(docPath, header.ToString());
        double ABankRestPosition =  7.00;
        double BBankRestPosition = -7.00;

        double position;
        Trajectory trajectory;

        for(int meterSet=0;meterSet<totalMu;meterSet++)
        {
            header.AppendLine($"Field = 0-{meterSet}");
            header.AppendLine($"Index = {(double) meterSet/(totalMu-1):0.0000}");
            header.AppendLine("Carriage Group = 1");
            header.AppendLine("Operator = ");
            header.AppendLine("Collimator = 0.0");

            // move from right to left
            // A bank, x2, trailing leaf
            // B bank, x1, leading leaf
            
            // A bank
            for(int leaf=1;leaf<=60;leaf++)
            {
                trajectory = trajectories[60-leaf][meterSet];
                position =  -ABankRestPosition + (14.0 / 280) * trajectory.TrailingLeafPos;
                if (leaf<10)
                {
                    header.AppendLine($"Leaf  {leaf}A = {position:0.000}");
                }
                else
                {
                    header.AppendLine($"Leaf {leaf}A = {position:0.000}");
                }
            }
            // B bank
            for(int leaf=1;leaf<=60;leaf++)
            {
                trajectory = trajectories[60-leaf][meterSet];
                position = -BBankRestPosition - (14.0 / 280) * trajectory.LeadingLeafPos;

                if (leaf<10)
                {
                    header.AppendLine($"Leaf  {leaf}B = {position:0.000}");
                }
                else
                {
                    header.AppendLine($"Leaf {leaf}B = {position:0.000}");
                }
            }

            header.AppendLine("Note = 0");
            header.AppendLine("Shape = 0");
            header.AppendLine("Magnification = 1.00");
            header.AppendLine();
        }

        header.AppendLine("CRC = 1234");

        File.WriteAllText(docPath, header.ToString());
    }