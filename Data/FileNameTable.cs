using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewEditor.Data
{
    public class FileNameTable
    {
        public byte[] data;
        public Dictionary<int, string> fileNames;
        public Dictionary<string, int> reverseFileNames;
        public int firstFile;

        public FileNameTable(byte[] data)
        {
            this.data = data;
            firstFile = HelperFunctions.ReadShort(data, 4);

            fileNames = new Dictionary<int, string>();
            reverseFileNames = new Dictionary<string, int>();

            int pos = 0;
            List<(int, string)> stack = new List<(int, string)> { (0, "") };

            while (stack.Count > 0)
            {
                pos = stack[0].Item1;
                string dir = stack[0].Item2;
                stack.RemoveAt(0);

                int fildID = HelperFunctions.ReadShort(data, pos + 4);
                pos = HelperFunctions.ReadInt(data, pos);

                while (data[pos] != 0)
                {
                    int len = data[pos] & 127;
                    bool subDir = data[pos] >= 128;
                    string name = Encoding.ASCII.GetString(data, pos + 1, len);

                    pos += len + 1;

                    if (subDir)
                    {
                        int id = HelperFunctions.ReadShort(data, pos) & 0xFFF;
                        stack.Add((id * 8, dir + name + "/"));
                        pos += 2;
                    }
                    else
                    {
                        fileNames.Add(fildID, dir + name);
                        reverseFileNames.Add(dir + name, fildID);
                        fildID++;
                    }
                }
            }
        }

        /// <summary>
        /// Rename one file, keeping its place in its directory so every file ID
        /// stays the same, and rebuild the table bytes. Returns null on success,
        /// or why the name was refused, in which case nothing is changed.
        /// </summary>
        public string RenameFile(string path, string newName)
        {
            if (!reverseFileNames.ContainsKey(path)) return "File not found.";
            if (string.IsNullOrEmpty(newName) || newName.Length > 127) return "Names must be 1 to 127 characters.";
            foreach (char c in newName)
                if (c < 0x21 || c > 0x7E || c == '/' || c == '\\') return "Names may only use letters, digits and punctuation, with no spaces or slashes.";
            int slash = path.LastIndexOf('/');
            string dir = slash < 0 ? "" : path.Substring(0, slash + 1);
            if (dir + newName == path) return null;
            if (reverseFileNames.ContainsKey(dir + newName)) return "A file with that name already exists in this folder.";

            // Directory and file entries keep their order: a directory's files are
            // numbered from its first file ID in listing order.
            int dirCount = HelperFunctions.ReadShort(data, 6);
            var entries = new List<(string name, bool subDir, int subDirID)>[dirCount];
            var firstFiles = new int[dirCount];
            var parents = new int[dirCount];
            var dirPaths = new string[dirCount];
            dirPaths[0] = "";
            int fileID;
            bool renamed = false;
            for (int d = 0; d < dirCount; d++)
            {
                firstFiles[d] = HelperFunctions.ReadShort(data, d * 8 + 4);
                parents[d] = HelperFunctions.ReadShort(data, d * 8 + 6);
                entries[d] = new List<(string, bool, int)>();
            }
            for (int d = 0; d < dirCount; d++)
            {
                int pos = HelperFunctions.ReadInt(data, d * 8);
                fileID = firstFiles[d];
                while (data[pos] != 0)
                {
                    int len = data[pos] & 127;
                    bool subDir = data[pos] >= 128;
                    string name = Encoding.ASCII.GetString(data, pos + 1, len);
                    pos += len + 1;
                    int subDirID = 0;
                    if (subDir)
                    {
                        subDirID = HelperFunctions.ReadShort(data, pos);
                        pos += 2;
                        dirPaths[subDirID & 0xFFF] = dirPaths[d] + name + "/";
                    }
                    else
                    {
                        if (dirPaths[d] + name == path && fileNames.TryGetValue(fileID, out string known) && known == path)
                        {
                            name = newName;
                            renamed = true;
                        }
                        fileID++;
                    }
                    entries[d].Add((name, subDir, subDirID));
                }
            }
            if (!renamed) return "File not found.";

            var main = new List<byte>(new byte[dirCount * 8]);
            var tables = new List<byte>();
            for (int d = 0; d < dirCount; d++)
            {
                HelperFunctions.WriteInt(main, d * 8, dirCount * 8 + tables.Count);
                main[d * 8 + 4] = (byte)firstFiles[d];
                main[d * 8 + 5] = (byte)(firstFiles[d] >> 8);
                main[d * 8 + 6] = (byte)parents[d];
                main[d * 8 + 7] = (byte)(parents[d] >> 8);
                foreach (var entry in entries[d])
                {
                    tables.Add((byte)(entry.name.Length | (entry.subDir ? 0x80 : 0)));
                    tables.AddRange(Encoding.ASCII.GetBytes(entry.name));
                    if (entry.subDir)
                    {
                        tables.Add((byte)entry.subDirID);
                        tables.Add((byte)(entry.subDirID >> 8));
                    }
                }
                tables.Add(0);
            }
            main.AddRange(tables);
            data = main.ToArray();

            int id = reverseFileNames[path];
            reverseFileNames.Remove(path);
            reverseFileNames[dir + newName] = id;
            fileNames[id] = dir + newName;
            return null;
        }

        public int NarcToFileID(int narcID)
        {
            string output = "a/";
            int onesPlace = narcID % 10;
            int tensPlace = narcID % 100 - onesPlace;
            int hundredsPlace = narcID - tensPlace - onesPlace;
            output += hundredsPlace.ToString()[0] + "/" + tensPlace.ToString()[0] + "/" + onesPlace.ToString()[0];
            return reverseFileNames[output];
        }
    }
}
