using PawnIO;
using System.Diagnostics;
using System.Text.RegularExpressions;

public static class NvidiaSmi
{

    public static int GetDefaultMaxGPUPower()
    {
        // HP OMEN models - GPU TGP defaults based on chassis tier
        if (AppConfig.IsOmenMax()) return 140;      // Flagship tier (RTX 4070/4080/4090)
        if (AppConfig.IsOmenTranscend()) return 90; // Thin-and-light (RTX 5060: 90W TGP)
        if (AppConfig.IsOmenSlim()) return 100;     // Slim chassis
        if (AppConfig.IsOmen16()) return 115;       // Standard OMEN 16

        return 115;
    }

    public static int GetMaxGPUPower()
    {
        string output = RunNvidiaSmiCommand("--query-gpu=power.max_limit --format csv,noheader,nounits");
        output = output.Trim().Trim('\n', '\r').Replace(".00","").Replace(",00", "");

        if (float.TryParse(output, out float floatValue))
        {
            int intValue = (int)floatValue;
            if (intValue >= 50 && intValue <= 175) return intValue;
        }

        return GetDefaultMaxGPUPower();
    }

    private static string RunNvidiaSmiCommand(string arguments = "-i 0 -q")
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            Arguments = arguments,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
        catch (Exception ex)
        {
            //return File.ReadAllText(@"smi.txt");
            Debug.WriteLine(ex.Message);
        }

        return "";

    }
}
