using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Gibbed.IO;
using NDesk.Options;

namespace DumpWand
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

            if (extras.Count != 1 || showHelp == true)
            {
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            string inputPath = extras[0];

            using (var input = File.OpenRead(inputPath))
            {
                const Endian endian = Endian.Big;

                var fileVersion = input.ReadValueU32(endian);
                var appVersion = input.ReadValueU32(endian);

                if (fileVersion < 2 || fileVersion > 6)
                {
                    throw new NotSupportedException("unsupported wand version");
                }

                uint profileCount, unknown;
                if (fileVersion >= 5)
                {
                    input.Position += 4; // skip u32
                    unknown = input.ReadValueU32(endian);
                    input.Position += 16; // skip 4 u32s
                    profileCount = input.ReadValueU32(endian);
                }
                else
                {
                    profileCount = input.ReadValueU32(endian);
                    unknown = input.ReadValueU32(endian);
                }

                var profiles = new Profile[profileCount];
                for (uint i = 0; i < profileCount; i++)
                {
                    profiles[i] = Profile.Read(input, endian, fileVersion);
                }
                var logProfile = Profile.Read(input, endian, fileVersion);
                var credentialCount = input.ReadValueU32(endian);
                var credentials = new Credential[credentialCount];
                for (uint i = 0; i < credentialCount; i++)
                {
                    credentials[i] = Credential.Read(input, endian, fileVersion);
                }

                foreach (var credential in profiles.SelectMany(p => p.Credentials).Concat(logProfile.Credentials))
                {
                    if (string.IsNullOrEmpty(credential.FormURL) == true)
                    {
                        continue;
                    }

                    var usernameIndex = Array.FindIndex(credential.Inputs, i => (i.Flags & FormInputFlags.Username) != 0);
                    var passwordIndex = Array.FindIndex(credential.Inputs, i => (i.Flags & FormInputFlags.Password) != 0);
                    if (passwordIndex >= 0)
                    {
                        Console.WriteLine($"URL ....: {credential.FormURL}");
                        if (usernameIndex >= 0)
                        {
                            Console.WriteLine($"Username: {credential.Inputs[usernameIndex].Value}");
                        }
                        Console.WriteLine($"Password: {credential.Inputs[passwordIndex].Unknown}");
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.WriteLine($"No password for {credential.FormURL}");
                        Console.WriteLine();
                    }
                }

                foreach (var credential in credentials)
                {
                    Console.WriteLine($"URL ....: {credential.URL}");
                    Console.WriteLine($"Username: {credential.Username}");
                    Console.WriteLine($"Password: {credential.Password}");
                    Console.WriteLine();
                }
            }
        }

        private struct Profile
        {
            public string Name;
            public byte Flags;
            public FormCredential[] Credentials;

            public static Profile Read(Stream input, Endian endian, uint version)
            {
                Profile instance;
                instance.Name = ReadEncryptedString(input, endian);
                instance.Flags = input.ReadValueU8();
                if (instance.Flags != 0 && instance.Flags != 1)
                {
                    throw new InvalidOperationException();
                }
                var credentialCount = input.ReadValueU32(endian);
                instance.Credentials = new FormCredential[credentialCount];
                for (uint i = 0; i < credentialCount; i++)
                {
                    instance.Credentials[i] = FormCredential.Read(input, endian, version);
                }
                return instance;
            }
        }

        private struct FormCredential
        {
            public LastModified LastModified;
            public string FormURL;
            public string SubmitName;
            public string Unknown03;
            public string SiteURL;
            public uint Unknown05;
            public uint Unknown06;
            public uint Unknown07;
            public uint Unknown08;
            public uint Unknown09;
            public uint Unknown10;
            public FormInput[] Inputs;

            public static FormCredential Read(Stream input, Endian endian, uint version)
            {
                FormCredential instance;
                instance.LastModified = LastModified.Read(input, endian, version);
                instance.FormURL = ReadEncryptedString(input, endian);
                instance.SubmitName = ReadEncryptedString(input, endian);

                if (version >= 4)
                {
                    instance.Unknown03 = ReadEncryptedString(input, endian);
                    instance.SiteURL = ReadEncryptedString(input, endian);
                }
                else
                {
                    instance.Unknown03 = default;
                    instance.SiteURL = default;
                }

                instance.Unknown05 = input.ReadValueU32(endian);
                instance.Unknown07 = input.ReadValueU32(endian);
                instance.Unknown08 = input.ReadValueU32(endian);
                instance.Unknown09 = input.ReadValueU32(endian);
                instance.Unknown10 = input.ReadValueU32(endian);
                instance.Unknown06 = input.ReadValueU32(endian);

                var inputCount = input.ReadValueU32(endian);
                instance.Inputs = new FormInput[inputCount];
                for (uint i = 0; i < inputCount; i++)
                {
                    instance.Inputs[i] = FormInput.Read(input, endian);
                }

                return instance;
            }
        }

        private struct LastModified
        {
            public uint Unknown0;
            public string Unknown1;
            public string TimeStamp;

            public static LastModified Read(Stream input, Endian endian, uint version)
            {
                if (version < 6)
                {
                    return default;
                }

                LastModified instance;
                instance.Unknown0 = input.ReadValueU32(endian);
                instance.Unknown1 = ReadEncryptedString(input, endian);
                instance.TimeStamp = ReadEncryptedString(input, endian);
                return instance;
            }
        }

        [Flags]
        private enum FormInputFlags : byte
        {
            None = 0,
            Password = 1 << 0,
            Unknown1 = 1 << 1,
            Unknown2 = 1 << 2,
            Username = 1 << 3,
            Unknown4 = 1 << 4,
            Unknown5 = 1 << 5,
            Unknown6 = 1 << 6,
            Unknown7 = 1 << 7,
        }

        private struct FormInput
        {
            public FormInputFlags Flags;
            public string Name;
            public string Value;
            public string Unknown;

            public static FormInput Read(Stream input, Endian endian)
            {
                FormInput instance;
                instance.Flags = (FormInputFlags)input.ReadValueU8();
                instance.Name = ReadEncryptedString(input, endian);
                instance.Value = ReadEncryptedString(input, endian);
                instance.Unknown = ReadEncryptedString(input, endian);
                return instance;
            }
        }

        private struct Credential
        {
            public LastModified LastModified;
            public string URL;
            public string Username;
            public string Password;

            public static Credential Read(Stream input, Endian endian, uint version)
            {
                Credential instance;
                instance.LastModified = LastModified.Read(input, endian, version);
                instance.URL = ReadEncryptedString(input, endian);
                instance.Username = ReadEncryptedString(input, endian);
                instance.Password = ReadEncryptedString(input, endian);
                return instance;
            }
        }

        private static byte[] ReadBytes(Stream input, Endian endian)
        {
            var length = input.ReadValueS32(endian);
            if (length == 0)
            {
                return null;
            }
            if (length < 0)
            {
                throw new InvalidOperationException();
            }
            return input.ReadBytes(length);
        }

        private static byte[] _SaltBytes = new byte[]
        {
            0x83, 0x7D, 0xFC, 0x0F, 0x8E, 0xB3, 0xE8, 0x69, 0x73, 0xAF, 0xFF,
        };

        private static byte[] ReadEncryptedBytes(Stream input, Endian endian)
        {
            var bytes = ReadBytes(input, endian);
            if (bytes == null)
            {
                return null;
            }
            using (var temp = new MemoryStream(bytes, false))
            {
                var saltedSize = temp.ReadValueS32(endian);
                if (saltedSize != 8)
                {
                    throw new InvalidOperationException();
                }
                var saltedBytes = temp.ReadBytes(saltedSize);
                var dataSize = temp.ReadValueS32(endian);
                if ((dataSize % 8) != 0)
                {
                    throw new InvalidOperationException();
                }
                var dataBytes = temp.ReadBytes(dataSize);

                byte[] md51, md52;
                using (var md5 = MD5.Create())
                {
                    md5.TransformBlock(_SaltBytes, 0, _SaltBytes.Length, _SaltBytes, 0);
                    md5.TransformFinalBlock(saltedBytes, 0, saltedBytes.Length);
                    md51 = (byte[])md5.Hash.Clone();
                }
                using (var md5 = MD5.Create())
                {
                    md5.TransformBlock(md51, 0, md51.Length, md51, 0);
                    md5.TransformBlock(_SaltBytes, 0, _SaltBytes.Length, _SaltBytes, 0);
                    md5.TransformFinalBlock(saltedBytes, 0, saltedBytes.Length);
                    md52 = (byte[])md5.Hash.Clone();
                }
                using (var tdes = TripleDES.Create())
                {
                    var keyBytes = new byte[md51.Length + 8];
                    Array.Copy(md51, 0, keyBytes, 0, md51.Length);
                    Array.Copy(md52, 0, keyBytes, md51.Length, 8);
                    var ivBytes = new byte[md52.Length - 8];
                    Array.Copy(md52, 8, ivBytes, 0, md52.Length - 8);
                    tdes.Key = keyBytes;
                    tdes.IV = ivBytes;
                    tdes.Mode = CipherMode.CBC;
                    using (var decryptor = tdes.CreateDecryptor())
                    {
                        dataBytes = decryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);
                    }
                }
                return dataBytes;
            }
        }

        private static string ReadEncryptedString(Stream input, Endian endian)
        {
            var bytes = ReadEncryptedBytes(input, endian);
            if (bytes == null)
            {
                return null;
            }
            using (var temp = new MemoryStream(bytes, false))
            {
                return temp.ReadString(bytes.Length, true, Encoding.Unicode);
            }
        }
    }
}
