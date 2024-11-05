    using System.Drawing;
    using System.Linq;
    using System.IO;
    using System;

    const int MLC_FIELD_LIMIT = 499;

    /// <summary>
    /// Digitize returns a 2-dimensional double array, takes import a bmp file, assumes 42 lines of leaf out of 60 pairs will be used, and 
    /// 14cm of leaf traversing distance, with 0.05cm leaf motion resolution, that is 280 columns of leaf position
    /// </summary>
    public static int[,] Digitize(string fileName, int targetHeight=42, int targetWidth=280, int leafPairs=60)
    {
        Bitmap bmp = new Bitmap(fileName);

        int[,] arr = new int[leafPairs, targetWidth];

        Bitmap outputBmp = new Bitmap(targetWidth, targetHeight);
        
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
        // for (int j=0;j<shift;j++)
        // {
        //     for (int i=0;i<outputBmp.Width;i++)
        //     {
        //         arr[j, i] = 0;
        //     }
        // }
        // extract array data from the resampled bmp for middle portion MLC map
        for (int j=0;j<outputBmp.Height;j++)            // loop over image rows
        {
            for (int i=0;i<outputBmp.Width;i++)         // loop over image columns
            {
                // notice the change in row/column for height/width; Bitmap.GetPixel method
                // also the reverse of black/white
                arr[j+shift, i] = 255 - outputBmp.GetPixel(i,j).R;
            }
        }
        // // bottom portion blanks (not irradiated)
        // for (int j=outputBmp.Height+shift;j<outputBmp.Height+shift+shift;j++)
        // {
        //     for (int i=0; i<outputBmp.Width;i++)
        //     {
        //         arr[j, i] = 0;
        //     }
        // }

        outputBmp.Save("./images/_grayscaled_resampled.bmp");
        //Console.WriteLine($"new image size: {outputBmp.Width} x {outputBmp.Height}");
        //Console.WriteLine($"new image resolution: {outputBmp.HorizontalResolution}, {outputBmp.VerticalResolution}");
        return arr;
    }

    public class MlcSequencer
    {
        // in this implementation, leaf is moving from right to left
        public List<int> TrailingLeafPositions = new ();
        public List<int> LeadingLeafPositions = new ();
        public List<int> LeadingLeafMu = new (); // MU meter set for leading leaf
        public List<int> TrailingLeafMu = new (); // MU meter set for trailing leaf

        public int TotalMu = 0;
        
        // constructor
        public MlcSequencer(List<int> intensityProfile)
        {
             // From Eq(6) in L.Ma et al paper: intensity profile should be patched with 0
            List<int> intensity = new ();
            // patch leading zero
            intensity.Add(0);

            // hard copy
            for (int i=0;i<intensityProfile.Count;i++)
            {
                intensity.Add(intensityProfile.ElementAt(i));
            }

            // patch trailing zero
            intensity.Add(0);

            // add MU trackers
            int leadingMu = 0;
            int trailingMu = 0;

            // add lists for positive and negative coefficients 
            List<int> PositiveCoefficients = new (); // incremental changes in intensity from one point along the profile to the next; positive
            List<int> NegativeCoefficients = new (); // incremental changes in intensity from one point along the profile to the next; negative

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

        internal int GetTotalMu() => TrailingLeafMu.Any() ? TrailingLeafMu.Last() : 0;
        
        // internal double GetTrailingMu()
        // {
        //    try
        //    {
        //     return (int) TrailingLeafMu.Last();
        //    }
        //    catch(InvalidOperationException)
        //    {
        //     return 0;
        //    }
        // }
        // internal double GetLeadingMu()
        // {
        //    try
        //    {
        //     return (int) LeadingLeafMu.Last();
        //    }
        //    catch(InvalidOperationException)
        //    {
        //     return 0;
        //    }
        // }
    }

    /// <summary>
    /// Trajectory structure, contains meterset, leading leaf position and trailing leaf position
    /// Gives a one instant record for a pair of leaf at a particular meter set
    /// </summary>
    public struct Trajectory
    {
        public int MeterSet {get; }
        public int LeadingLeafPos {get; set;}
        public int TrailingLeafPos {get; set;}    

        public Trajectory(int meter, int leading, int trailing)
        {
            MeterSet = meter;
            LeadingLeafPos = leading;
            TrailingLeafPos = trailing;
        }

        public override string ToString() => $"MeterSet, leading, trailing: {MeterSet}, {LeadingLeafPos}, {TrailingLeafPos}";

    }

    /// <summary>
    /// using unit MU increment
    /// also index position
    /// implementation of Ma et al, Phys. Med. Biol. Optimized leaf-setting algorithm
    /// </summary>
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
            int leadingPos = pair.LeadingLeafPositions.ElementAt(0);
            int trailingPos = pair.TrailingLeafPositions.ElementAt(0);

            int leadingPositionLength = pair.LeadingLeafPositions.Count;
            int trailingPositionLength = pair.TrailingLeafPositions.Count;

            while (mu<=pair.TotalMu)
            {   
                if (mu<pair.LeadingLeafMu.ElementAt(leadingIdx) & mu<pair.TrailingLeafMu.ElementAt(trailingIdx))
                {

                    // add trajectory
                    table.Add(new Trajectory(mu, leadingPos, trailingPos));
                }
                if (mu>=pair.LeadingLeafMu.ElementAt(leadingIdx) & mu<pair.TrailingLeafMu.ElementAt(trailingIdx))
                {
                    //add trajectory
                    table.Add(new Trajectory(mu, leadingPos, trailingPos));
                    //move leading leaf
                    if (leadingIdx < leadingPositionLength-1)
                    {
                        // move
                        leadingIdx++;
                    }
                    // if at the end, do not move
                    // update position
                    leadingPos = pair.LeadingLeafPositions.ElementAt(leadingIdx);
                    // add trajectory again
                    table.Add(new Trajectory(mu, leadingPos, trailingPos));
                }
                if (mu<pair.LeadingLeafMu.ElementAt(leadingIdx) & mu>=pair.TrailingLeafMu.ElementAt(trailingIdx))
                {
                    //add trajectory
                    table.Add(new Trajectory(mu, leadingPos, trailingPos));
                    //move trailing leaf
                    if (trailingIdx < trailingPositionLength-1)
                    {
                        // move
                        trailingIdx++;
                        trailingPos = pair.TrailingLeafPositions.ElementAt(trailingIdx);
                    }
                    else
                    {
                        trailingPos = leadingPos;
                    }
                    // add trajectory again
                    table.Add(new Trajectory(mu, leadingPos, trailingPos));
                }
                if (mu>=pair.LeadingLeafMu.ElementAt(leadingIdx) & mu>=pair.TrailingLeafMu.ElementAt(trailingIdx))
                {
                    //add trajectory
                    table.Add(new Trajectory(mu, leadingPos, trailingPos));

                    if (mu < pair.TotalMu)
                    {
                        // move leading leaf
                        if (leadingIdx < leadingPositionLength-1)
                        {
                            leadingIdx++;
                        }
                        // if at the end, do not move
                        // update position
                        leadingPos = pair.LeadingLeafPositions.ElementAt(leadingIdx);

                        //move trailing leaf
                        if (trailingIdx < trailingPositionLength-1)
                        {
                            // move
                            trailingIdx++;
                            trailingPos = pair.TrailingLeafPositions.ElementAt(trailingIdx);
                        }
                        else
                        {
                            trailingPos = pair.TrailingLeafPositions.Last();
                        }
                        // add trajectory again
                        table.Add(new Trajectory(mu, leadingPos, trailingPos));
                    }
                }
                mu++;
            }
            table.Add(new Trajectory(mu-1, leadingPos, leadingPos));
        }
        return table;
    }

    public static List<List<Trajectory>> CreateFieldTrajectoryTable(List<MlcSequencer> pairs, int fieldsLimit = MLC_FIELD_LIMIT)
    {
        List<List<Trajectory>> tables = new List<List<Trajectory>>();

        foreach(MlcSequencer pair in pairs)
        {
            List<Trajectory> seq = CreatePairTrajectoryTable(pair);

            if (seq.Count == 1) // static pair
            {
                for (int i=1;i<fieldsLimit;i++)
                {
                    seq.Add(new Trajectory(i, 0, 0));
                }
                
            }

            else if (seq.Count < fieldsLimit) // not enough pair
            {
                Trajectory lastTrajectory = seq.Last();
                for (int i=seq.Count;i<fieldsLimit;i++)
                {
                    seq.Add(new Trajectory(i, lastTrajectory.LeadingLeafPos, lastTrajectory.TrailingLeafPos));
                }
                
            }
            else
            {   
                throw new InvalidOperationException("Leaf pair trajectory file length more than limit!");
                // Console.WriteLine($"This should never appear! fieldsLimit = {fieldsLimit}");
                // Console.WriteLine($"The trajectory table from this pair length is {seq.Count}");
                // Console.WriteLine($"total intensity height: {pair.TotalMu}");
                // // sampling algorithm?
                // List<Trajectory> newSeq = new List<Trajectory>();
                // for (int i=0; i<fieldsLimit; i++)
                // {
                //     int idx = (int) i * (seq.Count / fieldsLimit);
                //     newSeq.Add(seq[idx]);
                // }
            }
            tables.Add(seq);
        }

        return tables;
    }

    public static List<MlcSequencer> CreatePairsFromArray(int[,] arr, int fieldsLimit, double compressionFactor, double incrementCF)
    {
        List<int> row;
        bool satisfied = false;
        bool reached = false;

        int maxTrajectoryLength;

        List<MlcSequencer> pairs = new List<MlcSequencer>();

        while ((!satisfied) | (!reached))
        {

            Console.WriteLine($"try compression factor {compressionFactor}");

            for(int i=0;i<arr.GetLength(0);i++)
            {
                row = Enumerable.Range(0, arr.GetLength(1))
                    .Select(x=>Convert.ToInt32(arr[i, x] * compressionFactor / 255))
                    .ToList();
                pairs.Add(new MlcSequencer(row));
            }

            // find out the max total trajectory length from all pairs
            maxTrajectoryLength = pairs.Select(x=>CreatePairTrajectoryTable(x).Count).Max();

            if (maxTrajectoryLength>fieldsLimit)
            {
                reached = true;
                compressionFactor -= incrementCF;
                pairs.Clear();
            }
            else
            {
                if (reached)
                {
                    satisfied = true;
                }
                else
                {
                    compressionFactor += incrementCF;
                    pairs.Clear();
                }
            }
        }
        Console.WriteLine($"Compression factor {compressionFactor}, completed! moving to next");
        return pairs;
    }
    
    public static List<List<Trajectory>> CreateFieldTrajectoryForImageFile(string imgFile)
    {
        int[,] arr = Digitize(imgFile);

        List<MlcSequencer> pairs = CreatePairsFromArray(arr, fieldsLimit:MLC_FIELD_LIMIT, compressionFactor:50.0, incrementCF: 5.0);

        List<List<Trajectory>> tables = CreateFieldTrajectoryTable(pairs);

        return tables;
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

    public static void WriteMlcFile(List<List<Trajectory>> trajectories, string outputMlcFile)
    {
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

        File.WriteAllText(outputMlcFile, header.ToString());
    }