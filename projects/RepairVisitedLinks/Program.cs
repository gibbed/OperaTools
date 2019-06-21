using System;
using System.Collections.Generic;
using System.IO;
using Gibbed.IO;
using NDesk.Options;

namespace RepairVisitedLinks
{
    internal class Program
    {
        private static string GetExecutableName()
        {
            return Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().CodeBase);
        }

        public static void Main(string[] args)
        {
            bool showHelp = false;

            var options = new OptionSet()
            {
                { "h|help", "show this message and exit", v => showHelp = v != null },
            };

            List<string> extras;

            try
            {
                extras = options.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("{0}: ", GetExecutableName());
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `{0} --help' for more information.", GetExecutableName());
                return;
            }

            if (extras.Count < 1 || extras.Count > 2 || showHelp == true)
            {
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            string inputPath = extras[0];
            string outputPath = extras.Count > 1 ? extras[1] : Path.ChangeExtension(inputPath, null) + "_FIXED.dat";

            using (var input = File.OpenRead(inputPath))
            using (var output = File.Create(outputPath))
            {
                const Endian endian = Endian.Big;

                var fileVersion = input.ReadValueU32(endian);
                var appVersion = input.ReadValueU32(endian);
                var idTagLength = input.ReadValueU16(endian);
                var lengthLength = input.ReadValueU16(endian);

                if (idTagLength != 1 || lengthLength != 2)
                {
                    Console.WriteLine(
                        "Bad lengths: id tag = {0} (expected 1), length = {1} (expected 2).",
                        idTagLength,
                        lengthLength);
                    return;
                }

                output.WriteValueU32(fileVersion, endian);
                output.WriteValueU32(appVersion, endian);
                output.WriteValueU16(idTagLength, endian);
                output.WriteValueU16(lengthLength, endian);

                while (input.Position < input.Length)
                {
                    Console.WriteLine("{0}", input.Position);

                    var idTag = input.ReadValueU8();
                    var length = input.ReadValueU16(endian);

                    var currentPosition = input.Position;
                    var nextPosition = currentPosition + length;

                    if (idTag != 2)
                    {
                        throw new InvalidOperationException();
                    }

                    if (nextPosition == input.Length)
                    {
                        output.WriteValueU8(idTag);
                        output.WriteValueU16(length, endian);
                        output.WriteFromStream(input, length);
                        continue;
                    }

                    input.Position = nextPosition;
                    var nextIdTag = input.ReadValueU8();
                    if (nextIdTag == 2)
                    {
                        // length is correct
                        input.Position = currentPosition;
                        output.WriteValueU8(idTag);
                        output.WriteValueU16(length, endian);
                        output.WriteFromStream(input, length);
                        continue;
                    }

                    var shift = lengthLength * 8;
                    uint? foundLength = null;
                    for (uint i = 0; i < 32; i++)
                    {
                        var testLength = length | (i << shift);
                        var nextTestPosition = currentPosition + testLength;

                        input.Position = nextTestPosition;
                        var nextTestIdTag = input.ReadValueU8();
                        if (nextTestIdTag == 2)
                        {
                            foundLength = testLength;
                            break;
                        }
                    }

                    if (foundLength.HasValue == false)
                    {
                        throw new InvalidOperationException();
                    }

                    var realLength = foundLength.Value;

                    Console.WriteLine("Attempting to repair @{0}", currentPosition);

                    input.Position = currentPosition;
                    using (var oldEntry = input.ReadToMemoryStream((int)realLength))
                    using (var newEntry = new MemoryStream())
                    {
                        while (oldEntry.Position < oldEntry.Length)
                        {
                            var localIdTag = oldEntry.ReadValueU8();
                            var localLength = oldEntry.ReadValueU16(endian);

                            if (localIdTag == 3 || // url
                                localIdTag == 4 || // time visited
                                localIdTag == 139) // was form request
                            {
                                newEntry.WriteValueU8(localIdTag);
                                newEntry.WriteValueU16(localLength, endian);
                                newEntry.WriteFromStream(oldEntry, localLength);
                            }
                            else if (localIdTag == 34) // anchor
                            {
                                oldEntry.Position += localLength;
                            }
                            else
                            {
                                throw new NotSupportedException();
                            }
                        }

                        newEntry.Flush();
                        newEntry.Position = 0;

                        if (newEntry.Length > 0xFFFF)
                        {
                            throw new InvalidOperationException();
                        }

                        var newLength = (ushort)newEntry.Length;
                        output.WriteValueU8(idTag);
                        output.WriteValueU16(newLength, endian);
                        output.WriteFromStream(newEntry, newLength);
                    }
                }
            }
        }
    }
}
