    using System.Drawing;
    using System.Linq;
    using System.IO;
    using System;

    /// <summary>
    /// Digitize returns a 2-dimensional double array, takes import a bmp file, assumes 42 lines of leaf out of 60 pairs will be used, and 
    /// 14cm of leaf traversing distance, with 0.05cm leaf motion resolution, that is 280 columns of leaf position
    /// compression factor is for setting up "proper" intensity latitude to make sure not exceeding MLC sequencing 500 limit
    /// </summary>
    public static double[,] Digitize(string fileName, double compressionFactor = 100.0, int targetHeight=42, int targetWidth=280, int leafPairs=60)
    {
        Bitmap bmp = new Bitmap(fileName);

        //double[,] arr = new double[targetHeight, targetWidth];

        double[,] arr = new double[leafPairs, targetWidth];

        Bitmap outputBmp = new Bitmap(targetWidth, targetHeight);
        
        double pv = 0;
        int shift = (int) (leafPairs-targetHeight)/2;
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

        // redraw the original bmp to the outputBmp with the targeting sizes of the MLC intensity map
        using (Graphics g = Graphics.FromImage(outputBmp))
        {
            g.DrawImage(bmp, new Rectangle(0, 0, targetWidth, targetHeight));
        }

        // top portion blanks (not irradiated)
        for (int j=0;j<shift;j++)
        {
            for (int i=0;i<outputBmp.Width;i++)
            {
                arr[j, i] = 0;
            }
        }
        // extract array data from the resampled bmp for middle portion MLC map
        for (int j=0;j<outputBmp.Height;j++)            // loop over image rows
        {
            for (int i=0;i<outputBmp.Width;i++)         // loop over image columns
            {
                //var pv = 255 - outputBmp.GetPixel(i, j).R;
                // also using a latitude modifier to give "proper" MU increment;
                // the MLC sequencing file total 500 positions, i.e., 500 MU meterset can be used;

                //var pv = (int) (255 - outputBmp.GetPixel(i,j).R) * (100 / 255);
                //arr[j,i] = pv;  // notice the change in row/column for height/width; Bitmap.GetPixel method
                pv = (double) (255 - outputBmp.GetPixel(i,j).R);
                pv /= 255.0;
                arr[j+shift, i] = pv * compressionFactor;
            }
        }
        // bottom portion blanks (not irradiated)
        for (int j=outputBmp.Height+shift;j<outputBmp.Height+shift+shift;j++)
        {
            for (int i=0; i<outputBmp.Width;i++)
            {
                arr[j, i] = 0;
            }
        }

        outputBmp.Save("./images/rw_grayscale_resampled.bmp");
        //Console.WriteLine($"new image size: {outputBmp.Width} x {outputBmp.Height}");
        //Console.WriteLine($"new image resolution: {outputBmp.HorizontalResolution}, {outputBmp.VerticalResolution}");
        return arr;
    }

    public class MlcSequencer
    {
        // in this implementation, leaf is moving from right to left
        public List<double> TrailingLeafPositions = new List<double>{};
        public List<double> LeadingLeafPositions = new List<double>{};
        public List<double> LeadingLeafMu = new List<double>{}; // MU meter set for leading leaf
        public List<double> TrailingLeafMu = new List<double>{}; // MU meter set for trailing leaf

        public double TotalMu = 0.0;
        
        // constructor
        public MlcSequencer(List<double> intensityProfile)
        {
             // From Eq(6) in L.Ma et al paper: intensity profile should be patched with 0
            List<double> intensity = new List<double>{0};

            // hard copy
            for (int i=0;i<intensityProfile.Count;i++)
            {
                intensity.Add(intensityProfile.ElementAt(i));
            }

            // patch trailing zero
            intensity.Add(0);

            // add MU trackers
            double leadingMu = 0;
            double trailingMu = 0;

            // add lists for positive and negative coefficients 
            List<double> PositiveCoefficients = new List<double>{}; // incremental changes in intensity from one point along the profile to the next; positive
            List<double> NegativeCoefficients = new List<double>{}; // incremental changes in intensity from one point along the profile to the next; negative

            for (int i=intensity.Count-2; i>-1; i--)
            {
                var val = intensity.ElementAt(i+1) - intensity.ElementAt(i);
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
            TotalMu = GetTotalMu();
        }

        internal double GetTotalMu()
        {
           try
           {
            return TrailingLeafMu.Last();
           }
           catch(InvalidOperationException)
           {
            return 0;
           }
        }
        internal double GetTrailingMu()
        {
           try
           {
            return TrailingLeafMu.Last();
           }
           catch(InvalidOperationException)
           {
            return 0;
           }
        }
        internal double GetLeadingMu()
        {
           try
           {
            return LeadingLeafMu.Last();
           }
           catch(InvalidOperationException)
           {
            return 0;
           }
        }
    }

    /// <summary>
    /// Trajectory structure, contains meterset, leading leaf position and trailing leaf position
    /// Gives a one instant record for a pair of leaf at a particular meter set
    /// </summary>
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
    /// implementation of Ma et al, Phys. Med. Biol. Optimized leaf-setting algorithm
    public static List<Trajectory> CreatePairTrajectoryTable(MlcSequencer pair)
    {
        // trajectory table is a list of trajectories
        List<Trajectory> table = new List<Trajectory>();
       
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

        Console.WriteLine($"field trajectory using total MU {totalFieldMu}, ideally this number getting close but smaller than {fieldsLimit}; if not, consider changing the compression factor (from 1 to 255)");

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

    public static List<MlcSequencer> CreatePairsForImageFile(string imgFile, int fieldsLimit=499)
    {
        List<double> row;
        bool unsatisfied = true;
        bool notReached = true;

        double compressionFactor = 5.0;
        int totalFieldMu;

        List<MlcSequencer> pairs = new List<MlcSequencer>();

        while (unsatisfied | notReached)
        {
            Console.WriteLine($"try compression factor {compressionFactor}");
            
            var arr = Digitize(imgFile, compressionFactor);

            for(int i=0;i<arr.GetLength(0);i++)
            {
                row = Enumerable.Range(0, arr.GetLength(1)).Select(x=>arr[i, x]).ToList();
                pairs.Add(new MlcSequencer(row));
            }

            // find out the max field mu for all pairs
            totalFieldMu = (int) pairs.Select(x=>x.TotalMu).Max();
            if (totalFieldMu>fieldsLimit)
            {
                unsatisfied = true;
                notReached = false;
                compressionFactor -= 5.0;
            }
            else
            {
                if (notReached)
                {
                    unsatisfied = true;
                }
                else
                {
                    unsatisfied = false;
                }
                compressionFactor += 5.0;
            }
        }
        Console.WriteLine($"Compression factor {compressionFactor}, completed! moving to next");
        return pairs;
    }
    
    public static List<List<Trajectory>> CreateFieldTrajectoryForImageFile(string imgFile)
    {
        List<double> row;
        List<MlcSequencer> pairs = CreatePairsForImageFile(imgFile);
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