using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FullBasic.Editor
{
    /// <summary>
    /// Imports <c>.bas</c> files as <see cref="TextAsset"/>s so they can be
    /// referenced from MonoBehaviours, ScriptableObjects, or AssetDatabase
    /// loads. The inspector for the imported asset (see
    /// <see cref="BasicAssetInspector"/>) adds a "Run" button and a preview.
    /// </summary>
    [ScriptedImporter(version: 1, ext: "bas")]
    public sealed class BasicScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var text = File.ReadAllText(ctx.assetPath);
            var asset = new TextAsset(text);
            ctx.AddObjectToAsset("main", asset);
            ctx.SetMainObject(asset);
        }
    }
}
