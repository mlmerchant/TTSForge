using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading.Tasks;

class Program
{
    static void Main(string[] args)
    {
        // Parse arguments into a dictionary
        Dictionary<string, List<string>> arguments = ParseArguments(args);

        // Validate required arguments
        if (!arguments.ContainsKey("--gender") || !arguments.ContainsKey("--outfile"))
        {
            Console.WriteLine("Usage: --gender <Male|Female> --outfile <OutputFile> (--string <Text> | --infile <InputFile>) [--parse <ParseFile> ...]");
            return;
        }

        if (arguments.ContainsKey("--string") && arguments.ContainsKey("--infile"))
        {
            Console.WriteLine("Error: Both --string and --infile cannot be used together. Please specify only one.");
            return;
        }

        if (!arguments.ContainsKey("--string") && !arguments.ContainsKey("--infile"))
        {
            Console.WriteLine("Error: You must specify either --string or --infile.");
            return;
        }

        string gender = arguments["--gender"].First();
        string outfile = arguments["--outfile"].First();

        // Determine filetype from outfile extension if --filetype is not specified
        string filetype;
        string extension = Path.GetExtension(outfile)?.ToLower();
        if (extension == ".mp3")
        {
            filetype = "mp3";
        }
        else if (extension == ".wav")
        {
            filetype = "wav";
        }
        else
        {
            filetype = "wav"; // Default to wav if no valid extension is provided
            outfile += ".wav"; // Add default extension if none is provided
        }

        string inputString = null;

        // Handle input string or input file
        if (arguments.ContainsKey("--string"))
        {
            inputString = arguments["--string"].First();
        }
        else if (arguments.ContainsKey("--infile"))
        {
            string infile = arguments["--infile"].First();
            if (!File.Exists(infile))
            {
                Console.WriteLine($"Error: Input file '{infile}' not found.");
                return;
            }

            // Read all lines from the input file
            string[] lines = File.ReadAllLines(infile);

            // Define special characters
            char[] specialCharacters = { '.', '!', '?', ';', ':' };

            // Process each line
            for (int i = 0; i < lines.Length; i++)
            {
                // Trim trailing spaces
                lines[i] = lines[i].TrimEnd();

                // Check if the line ends with a special character
                if (!string.IsNullOrWhiteSpace(lines[i]) && !specialCharacters.Contains(lines[i].Last()))
                {
                    lines[i] += ".";
                }
            }

            // Combine lines into a single string with spaces replacing newlines
            inputString = string.Join("  ", lines);
        }

        // Apply parsing rules from parse files, if any
        if (arguments.ContainsKey("--parse"))
        {
            foreach (string parseFile in arguments["--parse"])
            {
                if (!File.Exists(parseFile))
                {
                    Console.WriteLine($"Warning: Parse file '{parseFile}' not found. Skipping.");
                    continue;
                }

                inputString = ApplyParseRules(inputString, parseFile);
            }
        }

        inputString = inputString.Replace("\"", "'");

        // Validate gender input
        if (gender != "Male" && gender != "Female")
        {
            throw new ArgumentException("Invalid gender specified. Use 'Male' or 'Female'.");
        }

        // Ensure output directory exists
        string outDir = Path.GetDirectoryName(outfile);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        string tempWavFile = null;

        try
        {
            // Use a temporary file for WAV if converting to MP3
            tempWavFile = filetype == "mp3" ? Path.GetTempFileName() : outfile;

            // Generate WAV file
            using (SpeechSynthesizer synthesizer = new SpeechSynthesizer())
            {
                synthesizer.Rate = 0;

                if (gender == "Male")
                {
                    synthesizer.SelectVoiceByHints(VoiceGender.Male);
                }
                else
                {
                    synthesizer.SelectVoiceByHints(VoiceGender.Female);
                }

                synthesizer.SetOutputToWaveFile(tempWavFile);
                synthesizer.Speak(inputString);
            }

            // If filetype is mp3, convert using lame
            if (filetype == "mp3")
            {
                string lamePath = "lame"; // Assume it's in PATH
                if (!IsLameAvailable(lamePath))
                {
                    string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    lamePath = Path.Combine(exeDir, "lame.exe");

                    if (!IsLameAvailable(lamePath))
                    {
                        Console.WriteLine("Error: LAME encoder not found. Cannot create MP3 file.");
                        return;
                    }
                }

                string mp3File = Path.ChangeExtension(outfile, ".mp3");
                ConvertToMp3(lamePath, tempWavFile, mp3File).Wait();

                Console.WriteLine($"MP3 file saved to: {Path.GetFullPath(mp3File)}");
            }
            else
            {
                Console.WriteLine($"WAV file saved to: {Path.GetFullPath(outfile)}");
            }
        }
        finally
        {
            // Delete the temporary WAV file if it was created
            if (tempWavFile != null && tempWavFile != outfile && File.Exists(tempWavFile))
            {
                File.Delete(tempWavFile);
            }
        }
    }

    static bool IsLameAvailable(string lamePath)
    {
        try
        {
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = lamePath,
                    Arguments = "--help",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            })
            {
                process.Start();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }
        catch
        {
            return false;
        }
    }

    static async Task ConvertToMp3(string lamePath, string wavFile, string mp3File)
    {
        using (Process lameProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = lamePath,
                Arguments = $"-V2 \"{wavFile}\" \"{mp3File}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        })
        {
            lameProcess.Start();

            // Read output asynchronously to avoid deadlocks
            var outputTask = lameProcess.StandardOutput.ReadToEndAsync();
            var errorTask = lameProcess.StandardError.ReadToEndAsync();

            await Task.WhenAll(outputTask, errorTask);

            lameProcess.WaitForExit();

            if (lameProcess.ExitCode != 0)
            {
                Console.WriteLine("Error: LAME encoder failed.");
                throw new Exception(await errorTask);
            }
        }
    }

    static Dictionary<string, List<string>> ParseArguments(string[] args)
    {
        Dictionary<string, List<string>> parsedArgs = new Dictionary<string, List<string>>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                string key = args[i];
                string value = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;

                if (!parsedArgs.ContainsKey(key))
                {
                    parsedArgs[key] = new List<string>();
                }

                if (value != null)
                {
                    parsedArgs[key].Add(value);
                    i++; // Skip the value
                }
            }
        }

        return parsedArgs;
    }

    static string ApplyParseRules(string input, string parseFile)
    {
        foreach (string line in File.ReadAllLines(parseFile))
        {
            if (string.IsNullOrWhiteSpace(line) || !line.Contains("-->"))
            {
                continue;
            }

            string[] parts = line.Split(new[] { "-->" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                string original = parts[0];
                string replacement = parts[1];
                input = input.Replace(original, replacement);
            }
        }

        return input;
    }
}
