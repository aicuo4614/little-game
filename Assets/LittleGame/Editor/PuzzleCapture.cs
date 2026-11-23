using System;
using System.IO;
using UnityEngine;
using LittleGame;

internal static class PuzzleCapture
{
    public static object Capture(int width,int height)
    {
        if(width<16||width>2048||height<16||height>2048)throw new ArgumentOutOfRangeException("截图尺寸需16到2048");
        var camera=Camera.main;var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
        RenderTexture target=null;Texture2D image=null;
        try
        {
            target=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32);
            camera.targetTexture=target;
            GameBootstrap.View.Inspection.RenderSources(width,height);
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            image=new Texture2D(width,height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            var directory=Path.GetFullPath("Library/KabuUnityMcp/Captures");Directory.CreateDirectory(directory);
            var path=Path.Combine(directory,"game-"+Guid.NewGuid().ToString("N")+".png");File.WriteAllBytes(path,image.EncodeToPNG());
            return new {path,width,height,frame=Time.frameCount,note="编辑器实际设备操作区、回收层、环境及界面的合成渲染，非手机截图"};
        }
        finally
        {
            camera.targetTexture=oldTarget;RenderTexture.active=oldActive;
            GameBootstrap.View.Inspection.RestoreSources();
            if(image!=null)UnityEngine.Object.DestroyImmediate(image);
            if(target!=null)RenderTexture.ReleaseTemporary(target);
        }
    }
}
