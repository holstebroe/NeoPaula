using NeoPaula.Engine;

namespace NeoPaula.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                System.Console.WriteLine("Usage: NeoPaula.Console <mod_file>");
                return;
            }

            string filename = args[0];

            if (!File.Exists(filename))
            {
                System.Console.WriteLine($"File not found: {filename}");
                return;
            }

            try
            {
                using var player = new NeoPaulaPlayer();
                player.InterpolationMode = InterpolationMode.Linear;
                player.EnableOversampling = true;
                System.Console.WriteLine($"Playing: {filename} ...");
                player.PlayToEnd(filename);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error playing file: {ex.Message}");
            }
        }
    }
}
