using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 验证UI根节点字段改名后仍可恢复旧预制体中的层级引用
/// </summary>
public class UIRootSerializationTests
{
    /// <summary>
    /// 重新导入带旧字段名的真实预制体 验证四个挂载点均未丢失
    /// </summary>
    [Test]
    public void LegacyFieldNamesPreserveLayerReferences()
    {
        var folderName = "UIRootSerializationTest_" + Guid.NewGuid().ToString("N");
        var folderPath = "Assets/" + folderName;
        var prefabPath = folderPath + "/LegacyUIRoot.prefab";
        var fieldNames = new[] { "pageRoot", "popupRoot", "overlayRoot", "toastRoot" };
        GameObject root = null;
        GameObject loaded = null;
        try
        {
            AssetDatabase.CreateFolder("Assets", folderName);
            root = new GameObject("序列化兼容测试UI根节点");
            var rootType = typeof(GameComposition).Assembly.GetType("UIRoot", true);
            var component = root.AddComponent(rootType);
            var serialized = new SerializedObject(component);
            foreach (var name in fieldNames)
            {
                var child = new GameObject(name);
                child.transform.SetParent(root.transform, false);
                serialized.FindProperty("_" + name).objectReferenceValue = child.transform;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(PrefabUtility.SaveAsPrefabAsset(root, prefabPath), Is.Not.Null);
            Object.DestroyImmediate(root);
            root = null;

            var yaml = File.ReadAllText(prefabPath);
            foreach (var name in fieldNames)
            {
                var currentKey = "  _" + name + ":";
                Assert.That(yaml, Does.Contain(currentKey));
                yaml = yaml.Replace(currentKey, "  m_" + name + ":");
            }
            File.WriteAllText(prefabPath, yaml);
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            loaded = PrefabUtility.LoadPrefabContents(prefabPath);
            var restored = new SerializedObject(loaded.GetComponent(rootType));
            foreach (var name in fieldNames)
            {
                var reference = restored.FindProperty("_" + name).objectReferenceValue;
                Assert.That(reference, Is.EqualTo(loaded.transform.Find(name)), "旧字段引用未恢复:" + name);
            }
        }
        finally
        {
            if (loaded != null) PrefabUtility.UnloadPrefabContents(loaded);
            if (root != null) Object.DestroyImmediate(root);
            if (AssetDatabase.IsValidFolder(folderPath)) AssetDatabase.DeleteAsset(folderPath);
        }
    }
}
