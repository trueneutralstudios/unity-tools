// Editor window that allows you to copy one terrain onto another
// Created by True Neutral Studios

using UnityEngine;
using UnityEditor;

public class CopyTerrain : EditorWindow
{
    Terrain _from;
    Terrain _to;
    HeightCopyMode _heightMode = HeightCopyMode.ReplaceAll;
    HeightSampleMode _heightSampleMode = HeightSampleMode.Interpolated;
    SplatCopyMode _splatMode = SplatCopyMode.ReplaceAll;
    HolesCopyMode _holesMode = HolesCopyMode.ReplaceAll;

    [MenuItem("Tools/True Neutral Studios/Copy Terrain")]
    static void Init()
    {
        EditorWindow.GetWindow<CopyTerrain>().Show();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("--- Terrains ---");
        _from = EditorGUILayout.ObjectField("From", _from, typeof(Terrain), true) as Terrain;
        _to = EditorGUILayout.ObjectField("To", _to, typeof(Terrain), true) as Terrain;
        EditorGUILayout.LabelField("--- Settings ---");
        _heightMode = (HeightCopyMode)EditorGUILayout.EnumPopup("Height Mode:", _heightMode);
        if (_heightMode != HeightCopyMode.None) _heightSampleMode = (HeightSampleMode)EditorGUILayout.EnumPopup("    Height Sample Mode:", _heightSampleMode);
        _splatMode = (SplatCopyMode)EditorGUILayout.EnumPopup("Splat Mode:", _splatMode);
        _holesMode = (HolesCopyMode)EditorGUILayout.EnumPopup("Holes Mode: ", _holesMode);
        if (_from != null && _to != null) {
            if (GUILayout.Button("Confirm"))
            {
                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(_to.terrainData, "Modify Terrain");
                Undo.RegisterCompleteObjectUndo(_to.terrainData.alphamapTextures, "Modify Terrain");
                if (_heightMode != HeightCopyMode.None) _CopyHeight(_from, _to, _heightMode, _heightSampleMode);
                if (_splatMode != SplatCopyMode.None) _CopySplat(_from, _to, _splatMode);
                if (_holesMode != HolesCopyMode.None) _CopyHoles(_from, _to, _holesMode);
            }
        }
    }

    void _ConvertCoord(
        int fx, int fy, int fRes, Transform fTransform, Vector3 fSize,
        int tRes, Transform tTransform, Vector3 tSize,
        out float tx, out float ty, out bool inBounds
    ) {
        var localCenteredFPos = new Vector3(fx / (float)(fRes - 1) * fSize.x, 0, fy / (float)(fRes - 1) * fSize.z);
        var localCenteredTPos = tTransform.InverseTransformPoint(fTransform.TransformPoint(localCenteredFPos));
        tx = localCenteredTPos.x / tSize.x;
        ty = localCenteredTPos.z / tSize.z;
        inBounds = tx >= 0 && ty >= 0 && tx <= 1f && ty <= 1f;
    }

    void _ConvertCoord(
        int fx, int fy, int fRes, Transform fTransform, Vector3 fSize,
        int tRes, Transform tTransform, Vector3 tSize,
        out int tx, out int ty, out bool inBounds
    ) {
        _ConvertCoord(fx, fy, fRes, fTransform, fSize, tRes, tTransform, tSize, out float x, out float y, out inBounds);
        tx = Mathf.RoundToInt(x * (tRes - 1));
        ty = Mathf.RoundToInt(y * (tRes - 1));
    }

    void _GetCopyArea(
        int fRes, Transform fTransform, Vector3 fSize,
        int tRes, Transform tTransform, Vector3 tSize,
        out int minX, out int minY, out int maxX, out int maxY
    ) {
        _ConvertCoord(0, 0, fRes, fTransform, fSize, tRes, tTransform, tSize, out int x1, out int y1, out bool inbounds);
        _ConvertCoord(fRes - 1, fRes - 1, fRes, fTransform, fSize, tRes, tTransform, tSize, out int x2, out int y2, out inbounds);
        minX = Mathf.Clamp(Mathf.Min(x1, x2), 0, tRes - 1);
        minY = Mathf.Clamp(Mathf.Min(y1, y2), 0, tRes - 1);
        maxX = Mathf.Clamp(Mathf.Max(x1, x2), 0, tRes - 1);
        maxY = Mathf.Clamp(Mathf.Max(y1, y2), 0, tRes - 1);
    }

    private enum HeightCopyMode { None, ReplaceAll, ReplaceArea, Max, Min, Average }
    private enum HeightSampleMode { Rounded, Interpolated }

    void _CopyHeight(Terrain fromTerrain, Terrain toTerrain, HeightCopyMode mode = HeightCopyMode.ReplaceArea, HeightSampleMode sampleMode = HeightSampleMode.Rounded) {
        var fd = fromTerrain.terrainData;
        var fRes = fd.heightmapResolution;
        var fHeights = fd.GetHeights(0, 0, fd.heightmapResolution, fd.heightmapResolution);
        var td = toTerrain.terrainData;
        var tRes = td.heightmapResolution;
        var heightRatio = fd.size.y / td.size.y;
        var tHeights = (mode == HeightCopyMode.ReplaceAll) ? new float[td.heightmapResolution, td.heightmapResolution] : td.GetHeights(0, 0, td.heightmapResolution, td.heightmapResolution);
        _GetCopyArea(fRes, fromTerrain.transform, fd.size, tRes, toTerrain.transform, td.size, out int minX, out int minY, out int maxX, out int maxY);
        for(int tx = minX; tx < maxX; tx++) {
            for(int ty = minY; ty < maxY; ty++) {
                var fHeight = 0f;
                var inBounds = false;
                if (sampleMode == HeightSampleMode.Interpolated) {
                    _ConvertCoord(tx, ty, tRes, toTerrain.transform, td.size, fRes, fromTerrain.transform, fd.size, out float fx, out float fy, out inBounds);
                    if (!inBounds) continue;
                    fHeight = fd.GetInterpolatedHeight(fx, fy) / td.size.y;
                } else if (sampleMode == HeightSampleMode.Rounded) {
                    _ConvertCoord(tx, ty, tRes, toTerrain.transform, td.size, fRes, fromTerrain.transform, fd.size, out int fx, out int fy, out inBounds);
                    if (!inBounds) continue;
                    fHeight = Mathf.Clamp(fHeights[fy, fx] * heightRatio, 0f, 1f);
                }
                
                var tHeight = tHeights[ty, tx];
                switch(mode) {
                    case HeightCopyMode.Max:
                        tHeights[ty, tx] = Mathf.Max(tHeight, fHeight); break;
                    case HeightCopyMode.Min:
                        tHeights[ty, tx] = Mathf.Min(tHeight, fHeight); break;
                    case HeightCopyMode.Average:
                        tHeights[ty, tx] = (tHeight + fHeight) / 2f; break;
                    default:
                        tHeights[ty, tx] = fHeight; break;
                }
            }
        }
        td.SetHeights(0, 0, tHeights);
    }

    private enum SplatCopyMode { None, ReplaceAll, ReplaceArea }

    void _CopySplat(Terrain fromTerrain, Terrain toTerrain, SplatCopyMode mode) {
        var fd = fromTerrain.terrainData;
        var fRes = fd.alphamapResolution;
        float[,,] fAlphas = fd.GetAlphamaps(0, 0, fRes, fRes);
        var td = toTerrain.terrainData;
        var tRes = td.alphamapResolution;
        float[,,] tAlphas = td.GetAlphamaps(0, 0, tRes, tRes);
        if (mode == SplatCopyMode.ReplaceAll) {
            tAlphas = new float[tRes, tRes, fd.alphamapLayers];
            for(int i = 0; i < tRes; i++) for(int j = 0; j <tRes; j++) tAlphas[i, j, 0] = 1f;
        }
        _GetCopyArea(fRes, fromTerrain.transform, fd.size, tRes, toTerrain.transform, td.size, out int minX, out int minY, out int maxX, out int maxY);
        for(int layer = 0; layer < fd.alphamapLayers; layer++) {
            for(int tx = minX; tx < maxX; tx++) {
                for(int ty = minY; ty < maxY; ty++) {
                    _ConvertCoord(tx, ty, tRes, toTerrain.transform, td.size, fRes, fromTerrain.transform, fd.size, out int fx, out int fy, out bool inBounds);
                    if (!inBounds) continue;
                    tAlphas[ty, tx, layer] = fAlphas[fy, fx, layer];
                }
            }
        }
        td.terrainLayers = fd.terrainLayers;
        td.SetAlphamaps(0, 0, tAlphas);
    }

    private enum HolesCopyMode { None, ReplaceAll, ReplaceArea }

    void _CopyHoles(Terrain fromTerrain, Terrain toTerrain, HolesCopyMode mode) {
        var fd = fromTerrain.terrainData;
        var fRes = fd.holesResolution;
        bool[,] fHoles = fd.GetHoles(0, 0, fRes, fRes);
        var td = toTerrain.terrainData;
        var tRes = td.holesResolution;
        bool[,] tHoles = td.GetHoles(0, 0, tRes, tRes);
        if (mode == HolesCopyMode.ReplaceAll) {
            tHoles = new bool[tRes, tRes];
            for(int i = 0; i < tRes; i++) for(int j = 0; j <tRes; j++) tHoles[i, j] = true;
        }
        _GetCopyArea(fRes, fromTerrain.transform, fd.size, tRes, toTerrain.transform, td.size, out int minX, out int minY, out int maxX, out int maxY);
        for(int tx = minX; tx < maxX; tx++) {
            for(int ty = minY; ty < maxY; ty++) {
                _ConvertCoord(tx, ty, tRes, toTerrain.transform, td.size, fRes, fromTerrain.transform, fd.size, out int fx, out int fy, out bool inBounds);
                if (!inBounds) continue;
                tHoles[ty, tx] = fHoles[fy, fx];
            }
        }
        td.SetHoles(0, 0, tHoles);
    }
}
