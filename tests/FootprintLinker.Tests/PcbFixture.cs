using System.Text;

namespace FootprintLinker.Tests;

// Independently authored CFB v3 fixture: all streams use regular FAT sectors.
// Deliberately contains opaque data so extraction can be checked byte for byte.
internal static class PcbFixture
{
    private const uint End = 0xfffffffe, Free = 0xffffffff;
    public static void Write(string path, params string[] names)
    {
        var entries = new List<(string Name, byte Type, int Child, byte[]? Data)> { ("Root Entry",5,1,null),("Library",1,2,null) };
        using var index = new MemoryStream(); using (var w = new BinaryWriter(index, Encoding.UTF8, true))
        {
            var header = Encoding.ASCII.GetBytes("|HEADER=PCB 6.0 Binary Library File|WEIGHT=" + names.Length + "|\0");
            w.Write(header.Length); w.Write(header); w.Write(names.Length);
            foreach (var name in names) { var bytes = Encoding.Latin1.GetBytes(name); w.Write(bytes.Length+1); w.Write((byte)bytes.Length); w.Write(bytes); }
        }
        entries.Add(("Data",2,-1,index.ToArray()));
        foreach (var name in names) { int idx = entries.Count; entries.Add((name,1,idx+1,null)); entries.Add(("Data",2,-1,Encoding.ASCII.GetBytes("OPAQUE_GEOMETRY_"+name))); }
        var children = new Dictionary<int,int[]> { [0] = new[] {1}.Concat(Enumerable.Range(0,names.Length).Select(i=>3+i*2)).ToArray(), [1] = [2] };
        for (int i=0;i<names.Length;i++) children[3+i*2] = [4+i*2];
        var left = Enumerable.Repeat(-1,entries.Count).ToArray(); var right = (int[])left.Clone(); var child = (int[])left.Clone();
        int Tree(int[] ids) { if(ids.Length==0)return -1; int middle=ids.Length/2, id=ids[middle]; left[id]=Tree(ids[..middle]);right[id]=Tree(ids[(middle+1)..]);return id; }
        foreach(var pair in children) child[pair.Key]=Tree(pair.Value.OrderBy(i=>entries[i].Name.Length).ThenBy(i=>entries[i].Name,StringComparer.OrdinalIgnoreCase).ToArray());
        int directorySectors=(entries.Count*128+511)/512, streamCount=entries.Count(e=>e.Type==2), fatId=directorySectors+streamCount*8;
        var file=new byte[(fatId+2)*512]; var headerSpan=file.AsSpan(0,512);
        Convert.FromHexString("D0CF11E0A1B11AE1").CopyTo(headerSpan);
        void H16(int offset, ushort value)=>BitConverter.GetBytes(value).CopyTo(file,offset);
        void H32(int offset,uint value)=>BitConverter.GetBytes(value).CopyTo(file,offset);
        H16(24,0x3e);H16(26,3);H16(28,0xfffe);H16(30,9);H16(32,6);H32(44,1);H32(48,0);H32(56,4096);H32(60,End);H32(68,End);
        for(int i=0;i<109;i++)H32(76+i*4,Free);H32(76,(uint)fatId);
        var fat=Enumerable.Repeat(Free,128).ToArray();
        for(int i=0;i<directorySectors;i++)fat[i]=i==directorySectors-1?End:(uint)(i+1);
        int sector=directorySectors;
        for(int i=0;i<entries.Count;i++)
        {
            var e=entries[i]; int offset=512+i*128; var label=Encoding.Unicode.GetBytes(e.Name+"\0");label.CopyTo(file,offset);
            H16(offset+64,(ushort)label.Length);file[offset+66]=e.Type;file[offset+67]=1;
            H32(offset+68,(uint)left[i]);H32(offset+72,(uint)right[i]);H32(offset+76,(uint)child[i]);
            H32(offset+116,e.Type==2?(uint)sector:End);
            if(e.Type==2)
            {
                H32(offset+120,4096);e.Data!.CopyTo(file,(sector+1)*512);
                for(int j=0;j<8;j++)fat[sector+j]=j==7?End:(uint)(sector+j+1);sector+=8;
            }
        }
        fat[fatId]=0xfffffffd;for(int i=0;i<fat.Length;i++)H32((fatId+1)*512+i*4,fat[i]);
        File.WriteAllBytes(path,file);
    }
}
