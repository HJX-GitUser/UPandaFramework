using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UPandaGF;

namespace UPandaGF.Tests
{
using Object = UnityEngine.Object;

public class SerializationMigrationTests
{
    [Serializable] public class Payload
    {
        private int secret = 17;
        public int Secret => secret;
        public Dictionary<string, int> dictionary = new Dictionary<string, int>();
        public List<string> list = new List<string>();
        public int[] array;
        public string text;
        public bool flag;
        public long number;
        public decimal amount;
        public DateTime date;
        public TimeSpan span;
        public Guid guid;
        [NonSerialized] public Vector3 vector;
        [NonSerialized] public Quaternion rotation;
        public ABLoadPath location;
        [NonSerialized] public int transient;
    }
    [Serializable] public class Node { public string name;public Node next;public List<Node> children;public Dictionary<string,Node> links; }
    [Serializable] public class CallbackData
    {
        public static int callbacks,constructors;
        public int value;
        public CallbackData(){constructors++;}
        [OnDeserialized] private void Loaded(StreamingContext context){callbacks++;}
    }
    [Serializable] public class CustomSerialization : ISerializable
    {
        public static int executions;
        public CustomSerialization(){}
        protected CustomSerialization(SerializationInfo info,StreamingContext context){executions++;}
        public void GetObjectData(SerializationInfo info,StreamingContext context){info.AddValue("value",4);}
    }
    [Serializable] public class Container { public object value; }
    [Serializable] public class Animal { public string name; }
    [Serializable] public class Dog:Animal { public int age; }
    private string directory;
    private LogConfig previous;
    [SetUp] public void Setup()
    {
        directory=Path.Combine(Application.temporaryCachePath,"UPandaSerialization",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        previous=PLogger.cfg;PLogger.cfg=new LogConfig();
    }
    [TearDown] public void Cleanup()
    {
        PLogger.cfg=previous;
        if(Directory.Exists(directory)&&Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.Combine(Application.temporaryCachePath,"UPandaSerialization"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Directory.Delete(directory,true);
    }
    private static Payload Sample()
    {
        return new Payload{dictionary=new Dictionary<string,int>{{"你好",4},{"other",8}},list=new List<string>{"alpha",null,"末尾"},array=new[]{1,2,3},text="保存🌟",flag=true,
            number=long.MaxValue,amount=123456.125m,date=new DateTime(2025,5,3,4,5,6,DateTimeKind.Utc),span=TimeSpan.FromTicks(12345),guid=Guid.NewGuid(),
            vector=new Vector3(1.5f,-2,3),rotation=Quaternion.Euler(10,20,30),location=ABLoadPath.RemotePath,transient=99};
    }
    private static byte[] Legacy(object value)
    {
#pragma warning disable SYSLIB0011
        using(var stream=new MemoryStream()){new BinaryFormatter().Serialize(stream,value);return stream.ToArray();}
#pragma warning restore SYSLIB0011
    }
    private static T Decode<T>(byte[] data,bool expectedLegacy=false) where T:class
    {
        bool legacy;T value=VersionedDataSerializer.Deserialize<T>(data,"test",out legacy);Assert.AreEqual(expectedLegacy,legacy);return value;
    }
    private static void Equal(Payload expected,Payload actual)
    {
        Assert.IsNotNull(actual);Assert.AreEqual(expected.Secret,actual.Secret);CollectionAssert.AreEquivalent(expected.dictionary,actual.dictionary);CollectionAssert.AreEqual(expected.list,actual.list);
        CollectionAssert.AreEqual(expected.array,actual.array);Assert.AreEqual(expected.text,actual.text);Assert.AreEqual(expected.flag,actual.flag);Assert.AreEqual(expected.number,actual.number);
        Assert.AreEqual(expected.amount,actual.amount);Assert.AreEqual(expected.date,actual.date);Assert.AreEqual(expected.date.Kind,actual.date.Kind);Assert.AreEqual(expected.span,actual.span);
        Assert.AreEqual(expected.guid,actual.guid);Assert.AreEqual(expected.location,actual.location);Assert.AreEqual(0,actual.transient);
    }
    [Test] public void NewFormatPreservesPrivateFieldsContainersUnicodeAndValueTypes()
    {
        var sample=Sample();byte[] bytes=VersionedDataSerializer.Serialize(sample,"test");Assert.IsTrue(VersionedDataSerializer.IsVersioned(bytes));Equal(sample,Decode<Payload>(bytes));
    }
    [Serializable] public class SpatialData { public Vector3 position;public Quaternion rotation;public Color color;public Bounds bounds; }
    [Test] public void NewFormatSupportsExplicitUnityValueTypesWithoutSerializingComputedProperties()
    {
        var value=new SpatialData{position=Vector3.one,rotation=Quaternion.Euler(20,40,10),color=new Color(.1f,.2f,.3f,.4f),bounds=new Bounds(Vector3.one,Vector3.one*3)};
        var result=Decode<SpatialData>(VersionedDataSerializer.Serialize(value,"test"));
        Assert.AreEqual(value.position,result.position);Assert.AreEqual(value.rotation,result.rotation);Assert.AreEqual(value.color,result.color);Assert.AreEqual(value.bounds,result.bounds);
    }
    [Test] public void LegacyFieldsAndContainerValuesRemainReadableWithoutFormatter()
    {
        var sample=Sample();Equal(sample,Decode<Payload>(Legacy(sample),true));
    }
    [TestCase(false)] [TestCase(true)] public void SharedAndCyclicReferencesRemainIntact(bool legacy)
    {
        var root=new Node{name="root"};var child=new Node{name="child",next=root};root.next=child;root.children=new List<Node>{child,child};root.links=new Dictionary<string,Node>{{"child",child}};
        var restored=Decode<Node>(legacy?Legacy(root):VersionedDataSerializer.Serialize(root,"test"),legacy);
        Assert.AreSame(restored,restored.next.next);Assert.AreSame(restored.next,restored.children[0]);Assert.AreSame(restored.children[0],restored.children[1]);Assert.AreSame(restored.next,restored.links["child"]);
    }
    [TestCase(false)] [TestCase(true)] public void OrdinalIgnoreCaseDictionaryPreservesComparer(bool legacy)
    {
        var sample=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase){{"Case",7}};
        var restored=Decode<Dictionary<string,int>>(legacy?Legacy(sample):VersionedDataSerializer.Serialize(sample,"test"),legacy);Assert.AreEqual(7,restored["case"]);
    }
    [TestCase(false)] [TestCase(true)] public void ReaderDoesNotExecuteConstructorsOrDeserializationCallbacks(bool legacy)
    {
        var value=new CallbackData{value=4};byte[] bytes=legacy?Legacy(value):VersionedDataSerializer.Serialize(value,"test");CallbackData.callbacks=CallbackData.constructors=0;
        Assert.AreEqual(4,Decode<CallbackData>(bytes,legacy).value);Assert.AreEqual(0,CallbackData.callbacks);Assert.AreEqual(0,CallbackData.constructors);
    }
    [Test] public void CustomSerializationIsRejectedWithoutExecutingItsReader()
    {
        byte[] bytes=Legacy(new CustomSerialization());CustomSerialization.executions=0;
        Assert.That(()=>Decode<CustomSerialization>(bytes,true),Throws.Exception);Assert.AreEqual(0,CustomSerialization.executions);
    }
    [Test] public void KnownPolymorphicTypeIsExplicitlyRegistered()
    {
        VersionedDataSerializer.RegisterKnownType<Dog>();var sample=new Container{value=new Dog{name="dog",age=3}};
        var restored=Decode<Container>(VersionedDataSerializer.Serialize(sample,"test"));Assert.AreEqual(3,((Dog)restored.value).age);
    }
    [Test] public void UnsupportedValueCannotOverwriteExistingSave()
    {
        string path=Path.Combine(directory,"data");byte[] before=VersionedDataSerializer.Serialize(Sample(),"test");VersionedDataSerializer.WriteFile(path,before);
        var go=new GameObject("CannotPersistUnityObject");
        try{Assert.That(()=>VersionedDataSerializer.WriteFile(path,VersionedDataSerializer.Serialize(new Container{value=go},"test")),Throws.Exception);CollectionAssert.AreEqual(before,File.ReadAllBytes(path));}
        finally{Object.DestroyImmediate(go);}
    }
    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] public void InvalidEnvelopeIsRejectedBeforeObjectCreation(int mode)
    {
        byte[] bytes=VersionedDataSerializer.Serialize(Sample(),"test");
        if(mode==0)bytes=bytes.Take(9).ToArray();
        if(mode==1)bytes[12]=99;
        if(mode==2)bytes[bytes.Length-1]^=1;
        if(mode==3)bytes=bytes.Take(bytes.Length-1).ToArray();
        if(mode==4)bytes=bytes.Concat(new byte[]{1}).ToArray();
        if(mode==5)bytes=Repack(bytes,new byte[]{20,99,0,0,0});
        Assert.That(()=>Decode<Payload>(bytes),Throws.Exception);
    }
    private static byte[] Repack(byte[] source,byte[] payload)
    {
        using(var input=new BinaryReader(new MemoryStream(source)))
        using(var output=new MemoryStream())
        using(var writer=new BinaryWriter(output))
        using(var sha=SHA256.Create())
        {writer.Write(input.ReadBytes(12));writer.Write(input.ReadInt32());writer.Write(input.ReadString());writer.Write(input.ReadString());writer.Write(payload.Length);writer.Write(sha.ComputeHash(payload));writer.Write(payload);return output.ToArray();}
    }
    [Test] public void CategoryAndRootTypeMustMatch()
    {
        byte[] bytes=VersionedDataSerializer.Serialize(Sample(),"test");bool legacy;
        Assert.That(()=>VersionedDataSerializer.Deserialize<Payload>(bytes,"assetref",out legacy),Throws.Exception);
        Assert.That(()=>VersionedDataSerializer.Deserialize<Node>(bytes,"test",out legacy),Throws.Exception);
    }
    [Test] public void OversizedArrayDeclarationIsRejectedWithoutAllocatingIt()
    {
        byte[] source=VersionedDataSerializer.Serialize(new int[]{1},"test");
        using(var payload=new MemoryStream())using(var writer=new BinaryWriter(payload))
        {writer.Write((byte)22);writer.Write(1);writer.Write("System.Int32[]");writer.Write(1);writer.Write(int.MaxValue);Assert.That(()=>Decode<int[]>(Repack(source,payload.ToArray())),Throws.Exception);}
    }
    [Test] public void FileTypeNamesCannotSelectArbitraryRuntimeTypes()
    {
        byte[] source=VersionedDataSerializer.Serialize(new Container(),"test");
        using(var payload=new MemoryStream())using(var writer=new BinaryWriter(payload))
        {writer.Write((byte)21);writer.Write(1);writer.Write("System.Diagnostics.Process");writer.Write(0);Assert.That(()=>Decode<Container>(Repack(source,payload.ToArray())),Throws.Exception);}
    }
    [Test] public void LegacyZeroCapacityPaddingIsAcceptedButOtherTrailingDataIsNot()
    {
        byte[] legacy=Legacy(Sample());Assert.IsNotNull(Decode<Payload>(legacy.Concat(new byte[1024]).ToArray(),true));
        Assert.That(()=>Decode<Payload>(legacy.Concat(new byte[]{7}).ToArray(),true),Throws.Exception);
    }
    [Test] public void LegacyFileIsBackedUpAndRewrittenOnlyAfterSuccessfulConversion()
    {
        string path=Path.Combine(directory,"legacy.binary");byte[] original=Legacy(Sample());File.WriteAllBytes(path,original);
        var value=VersionedDataSerializer.LoadFile<Payload>(path,"test");Assert.IsNotNull(value);Assert.IsTrue(VersionedDataSerializer.IsVersioned(File.ReadAllBytes(path)));
        CollectionAssert.AreEqual(original,File.ReadAllBytes(path+".legacy.bak"));Assert.IsNotNull(VersionedDataSerializer.LoadFile<Payload>(path,"test"));
        Assert.AreEqual(1,Directory.GetFiles(directory,"*.bak").Length);
    }
    [Test] public void ReadOnlyLegacyModeDoesNotRewriteSource()
    {
        string path=Path.Combine(directory,"read-only.binary");byte[] bytes=Legacy(Sample());File.WriteAllBytes(path,bytes);
        Assert.IsNotNull(VersionedDataSerializer.LoadFile<Payload>(path,"test",false));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));Assert.IsFalse(File.Exists(path+".legacy.bak"));
    }
    [Test] public void CorruptFileIsPreservedWithoutMigrationBackup()
    {
        string path=Path.Combine(directory,"broken.binary");byte[] bytes={0,1,2};File.WriteAllBytes(path,bytes);
        Assert.That(()=>VersionedDataSerializer.LoadFile<Payload>(path,"test"),Throws.Exception);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));Assert.IsFalse(File.Exists(path+".legacy.bak"));
    }
    [Test] public void ExistingBackupIsNotOverwritten()
    {
        string path=Path.Combine(directory,"legacy.binary");byte[] first=Legacy(new Payload{text="first"});File.WriteAllBytes(path+".legacy.bak",first);
        byte[] second=Legacy(new Payload{text="second"});File.WriteAllBytes(path,second);Assert.AreEqual("second",VersionedDataSerializer.LoadFile<Payload>(path,"test").text);
        CollectionAssert.AreEqual(first,File.ReadAllBytes(path+".legacy.bak"));Assert.AreEqual(2,Directory.GetFiles(directory,"*.bak").Length);
    }
    [Test] public void BinaryDataMgrKeepsPublicApiAndMigratesLegacySave()
    {
        var field=typeof(BinaryDataMgr).GetField("SAVE_PATH",BindingFlags.Static|BindingFlags.NonPublic);var extension=typeof(BinaryDataMgr).GetField("FILE_EXTENSION",BindingFlags.Static|BindingFlags.NonPublic);
        object previousPath=field.GetValue(null),previousExtension=extension.GetValue(null);field.SetValue(null,directory);extension.SetValue(null,".binary");
        try
        {
            var manager=new BinaryDataMgr();string path=Path.Combine(directory,"sample.binary");byte[] legacy=Legacy(Sample());File.WriteAllBytes(path,legacy);
            Assert.IsNotNull(manager.Load<Payload>("sample"));CollectionAssert.AreEqual(legacy,File.ReadAllBytes(path+".legacy.bak"));
            manager.Save(new Payload{text="new"},"sample");Assert.AreEqual("new",manager.Load<Payload>("sample").text);Assert.IsTrue(VersionedDataSerializer.IsVersioned(File.ReadAllBytes(path)));
        }
        finally{field.SetValue(null,previousPath);extension.SetValue(null,previousExtension);}
    }
    [TestCase(false)] [TestCase(true)] public void RegisteredDerivedRootCanBeLoadedThroughBaseType(bool legacy)
    {
        VersionedDataSerializer.RegisterKnownType<Dog>();var sample=new Dog{name="root",age=5};
        var restored=Decode<Animal>(legacy?Legacy(sample):VersionedDataSerializer.Serialize(sample,"test"),legacy);Assert.AreEqual(5,((Dog)restored).age);
    }
    [TestCase(false)] [TestCase(true)] public void RectangularAndPolymorphicArrayShapesSurvive(bool legacy)
    {
        VersionedDataSerializer.RegisterKnownType<int[,]>();VersionedDataSerializer.RegisterKnownType<string[]>();
        var array=new object[]{new int[,]{{1,2},{3,4}},new string[]{"a",null,"b"}};
        var restored=Decode<object[]>(legacy?Legacy(array):VersionedDataSerializer.Serialize(array,"test"),legacy);
        Assert.AreEqual(4,((int[,])restored[0])[1,1]);CollectionAssert.AreEqual((string[])array[1],(string[])restored[1]);
    }
    [Test] public void InvalidKnownTypeRegistrationCannotPoisonOtherOperations()
    {
        Assert.That(()=>VersionedDataSerializer.RegisterKnownType<Action>(),Throws.Exception);Assert.IsNotNull(Decode<Payload>(VersionedDataSerializer.Serialize(Sample(),"test")));
    }
    [Test] public void MetadataLimitsAreMeasuredInUtf8Bytes()
    {
        Assert.That(()=>VersionedDataSerializer.Serialize(Sample(),new string('类',100)),Throws.Exception);
    }
    [TestCase(false)] [TestCase(true)] public void EncryptedManifestRetainsBothFormatCompatibility(bool legacy)
    {
        var go=new GameObject("EncryptedManifestReader");var loader=go.AddComponent<AssetsLoader>();
        loader.AssetAESConfig=new AssetBundleClassificationWindowConfig{enable=true,AESKEY="1234567890123456",AESIV="6543210987654321"};
        try
        {
            byte[] original=legacy?Legacy(Manifest()):VersionedDataSerializer.Serialize(Manifest(),"assetref");
            byte[] encrypted=AESEncryption.AESEncrypt(original,loader.AssetAESConfig.AESKEY,loader.AssetAESConfig.AESIV);
            Assert.AreEqual("Test",loader.LoadABSourcesRelated(encrypted).sourcesDic["Assets/Test.prefab"].sourceName);
        }
        finally{Object.DestroyImmediate(go);}
    }
    [Test] public void MigrationDetectsSourceChangeInsteadOfOverwritingNewerFile()
    {
        string path=Path.Combine(directory,"changed");byte[] old=Legacy(new Payload{text="old"}),current=Legacy(new Payload{text="newer"});File.WriteAllBytes(path,current);
        Assert.That(()=>VersionedDataSerializer.MigrateFile(path,old,VersionedDataSerializer.Serialize(Sample(),"test")),Throws.Exception);CollectionAssert.AreEqual(current,File.ReadAllBytes(path));Assert.IsFalse(File.Exists(path+".legacy.bak"));
    }
    [Test] public void TruncatedAndMutatedLegacyRecordsFailWithoutExecutingCallbacks()
    {
        byte[] valid=Legacy(new CallbackData{value=7});CallbackData.callbacks=0;
        for(int length=0;length<valid.Length;length+=Math.Max(1,valid.Length/20))Assert.That(()=>Decode<CallbackData>(valid.Take(length).ToArray(),true),Throws.Exception);
        byte[] unsupported={0,1,0,0,0,255,255,255,255,1,0,0,0,0,0,0,0,21};Assert.That(()=>Decode<CallbackData>(unsupported,true),Throws.Exception);Assert.AreEqual(0,CallbackData.callbacks);
    }
    [Test] public void UnknownFutureFileDoesNotFallbackToLegacyReaderOrRewriteAnything()
    {
        string path=Path.Combine(directory,"future");byte[] bytes=VersionedDataSerializer.Serialize(Sample(),"test");bytes[12]=2;File.WriteAllBytes(path,bytes);
        Assert.That(()=>VersionedDataSerializer.LoadFile<Payload>(path,"test"),Throws.Exception);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));Assert.IsFalse(File.Exists(path+".legacy.bak"));
    }
    [Test] public void SavingBeforeLoadingStillBacksUpLegacyBytes()
    {
        string path=Path.Combine(directory,"direct-save");byte[] original=Legacy(new Payload{text="legacy"});File.WriteAllBytes(path,original);
        VersionedDataSerializer.SaveFile(path,new Payload{text="replacement"},"test");Assert.AreEqual("replacement",VersionedDataSerializer.LoadFile<Payload>(path,"test").text);
        CollectionAssert.AreEqual(original,File.ReadAllBytes(path+".legacy.bak"));
    }
    [TestCase(false)] [TestCase(true)] public void SharedManifestWriterBacksUpOldBytesIncludingEncryptedInput(bool encrypted)
    {
        string path=Path.Combine(directory,"assetData.assetref");var config=new AssetBundleClassificationWindowConfig{enable=encrypted,AESKEY="1234567890123456",AESIV="6543210987654321"};
        byte[] original=Legacy(Manifest());if(encrypted)original=AESEncryption.AESEncrypt(original,config.AESKEY,config.AESIV);File.WriteAllBytes(path,original);
        AssetReferenceSerializer.SaveFile(path,Manifest(),config);Assert.IsNotNull(AssetReferenceSerializer.Decode(File.ReadAllBytes(path),config));CollectionAssert.AreEqual(original,File.ReadAllBytes(path+".legacy.bak"));
        AssetReferenceSerializer.SaveFile(path,Manifest(),config);Assert.AreEqual(1,Directory.GetFiles(directory,"*.bak").Length);
    }
    [Test] public void ChangingEncryptionConfigurationDoesNotBlockRegenerationOrLoseOriginal()
    {
        string path=Path.Combine(directory,"assetData.assetref");AssetReferenceSerializer.SaveFile(path,Manifest());byte[] original=File.ReadAllBytes(path);
        var config=new AssetBundleClassificationWindowConfig{enable=true,AESKEY="1234567890123456",AESIV="6543210987654321"};
        AssetReferenceSerializer.SaveFile(path,Manifest(),config);CollectionAssert.AreEqual(original,File.ReadAllBytes(path+".legacy.bak"));Assert.IsNotNull(AssetReferenceSerializer.Decode(File.ReadAllBytes(path),config));
        byte[] encrypted=File.ReadAllBytes(path);config.enable=false;AssetReferenceSerializer.SaveFile(path,Manifest(),config);Assert.IsNotNull(AssetReferenceSerializer.Decode(File.ReadAllBytes(path)));
        Assert.IsTrue(Directory.GetFiles(directory,"*.bak").Any(file=>File.ReadAllBytes(file).SequenceEqual(encrypted)));
    }
    [Test] public void InvalidManifestCannotReplacePreviousValidFile()
    {
        string path=Path.Combine(directory,"assetData.assetref");AssetReferenceSerializer.SaveFile(path,Manifest());byte[] original=File.ReadAllBytes(path);var invalid=Manifest();invalid.bundleInfo.Clear();
        Assert.That(()=>AssetReferenceSerializer.SaveFile(path,invalid),Throws.Exception);CollectionAssert.AreEqual(original,File.ReadAllBytes(path));
    }
    [TestCase(false,DateTimeKind.Utc)] [TestCase(false,DateTimeKind.Local)] [TestCase(false,DateTimeKind.Unspecified)]
    [TestCase(true,DateTimeKind.Utc)] [TestCase(true,DateTimeKind.Local)] [TestCase(true,DateTimeKind.Unspecified)]
    public void DateTimeKindsAndWallClockTicksArePreserved(bool legacy,DateTimeKind kind)
    {
        var sample=Sample();sample.date=new DateTime(2023,5,6,7,8,9,kind);
        var restored=Decode<Payload>(legacy?Legacy(sample):VersionedDataSerializer.Serialize(sample,"test"),legacy);
        Assert.AreEqual(sample.date.Ticks,restored.date.Ticks);Assert.AreEqual(kind,restored.date.Kind);
    }
    [Serializable] public class GenericBox<T> { public T value; }
    [Test] public void GenericFieldIdentityDoesNotDependOnAssemblyVersion()
    {
        var bytes=VersionedDataSerializer.Serialize(new GenericBox<Animal>{value=new Animal{name="stable"}},"test");
        StringAssert.DoesNotContain("Version=",Encoding.UTF8.GetString(bytes));
        Assert.AreEqual("stable",Decode<GenericBox<Animal>>(bytes).value.name);
    }
    [Test] public void RegisteringUnityValueDtoDoesNotBlockUnrelatedLegacyReads()
    {
        VersionedDataSerializer.RegisterKnownType<SpatialData>();
        Assert.AreEqual("legacy",Decode<Payload>(Legacy(new Payload{text="legacy"}),true).text);
    }
    private static ABSourcesRelated Manifest()
    {
        var data=new ABSourcesRelated{mainBundleInfo=new AssetBundleLoadInfo{bundleName="Main",loadPath=ABLoadPath.StreamingAssetsPath}};
        data.bundleInfo.Add("common",new AssetBundleLoadInfo{bundleName="common",size=12,md5=new string('a',32),loadPath=ABLoadPath.RemotePath});
        data.sourcesDic.Add("Assets/Test.prefab",new AssetRelatedArg("common","Test"));return data;
    }
    [TestCase(false)] [TestCase(true)] public void RuntimeManifestLoaderReadsBothFormats(bool legacy)
    {
        var go=new GameObject("ManifestReader");var loader=go.AddComponent<AssetsLoader>();
        try{var data=Manifest();var read=loader.LoadABSourcesRelated(legacy?Legacy(data):VersionedDataSerializer.Serialize(data,"assetref"));Assert.IsNotNull(read);Assert.AreEqual("Test",read.sourcesDic["Assets/Test.prefab"].sourceName);Assert.AreEqual(ABLoadPath.RemotePath,read.bundleInfo["common"].loadPath);}
        finally{Object.DestroyImmediate(go);}
    }
    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] public void InvalidManifestRelationsAreRejected(int mode)
    {
        var data=Manifest();
        if(mode==0)data.sourcesDic["Assets/Test.prefab"].bundleName="missing";
        if(mode==1)data.bundleInfo["common"].size=-1;
        if(mode==2)data.mainBundleInfo.bundleName="../escape";
        if(mode==3)data.bundleInfo["common"].loadPath=(ABLoadPath)99;
        Assert.That(()=>AssetReferenceValidation.Validate(data),Throws.Exception);
    }
    [TestCase("Build")] [TestCase("Inspect")] public void ActualEditorCacheTypesMigrateIncludingPrivateCollections(string tab)
    {
#if UNITY_EDITOR
        var assembly=Assembly.Load("UPandaGF.Editor");string typeName=tab=="Build"?"UPandaGF.AssetBundleBrowser.AssetBundleBuildTab+BuildTabData":"UPandaGF.AssetBundleBrowser.AssetBundleInspectTab+InspectTabData";
        Type type=assembly.GetType(typeName,true);object value=Activator.CreateInstance(type,true);
        if(tab=="Build"){type.GetField("m_OutputPath").SetValue(value,"输出目录/AB");type.GetField("m_OnToggles").SetValue(value,new List<string>{"ToggleA"});}
        else{type.GetMethod("AddPath",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,new object[]{"Assets/TestBundle"});type.GetMethod("AddFolder",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,new object[]{"Assets/TestFolder"});}
        string path=Path.Combine(directory,"editor.dat");byte[] original=Legacy(value);File.WriteAllBytes(path,original);
        string kind=tab=="Build"?"ab-build-settings":"ab-inspect-settings";
        var method=typeof(VersionedDataSerializer).GetMethod("LoadFile").MakeGenericMethod(type);object restored=method.Invoke(null,new object[]{path,kind,true});
        Assert.IsNotNull(restored);Assert.IsTrue(VersionedDataSerializer.IsVersioned(File.ReadAllBytes(path)));CollectionAssert.AreEqual(original,File.ReadAllBytes(path+".legacy.bak"));
        if(tab=="Build")Assert.AreEqual("输出目录/AB",type.GetField("m_OutputPath").GetValue(restored));
        else Assert.AreEqual(1,((System.Collections.ICollection)type.GetField("m_BundleFolders",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(restored)).Count);
#endif
    }
}

}