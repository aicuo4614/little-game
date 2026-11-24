using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace LittleGame
{
    // 独立透明操作区只裁剪设备，UI、环境和回收栏保持原尺寸。飞出零件单独合成。
    public sealed class InspectionViewport : MonoBehaviour
    {
        public const int DeviceLayer=8,FlightLayer=9,TargetLayer=10;
        public Rect Region {get;private set;}
        private Camera main,deviceCamera,flightCamera;
        private RawImage deviceImage,flightImage;
        private RectTransform flightMask;
        private RenderTexture deviceTexture,flightTexture;
        private int width,height;
        private float allocatedHeight;
        public void Build(Camera source)
        {
            main=source;main.cullingMask&=~((1<<DeviceLayer)|(1<<FlightLayer)|(1<<TargetLayer));
            deviceCamera=MakeCamera("Inspection Camera",DeviceLayer,-2);
            flightCamera=MakeCamera("Recovery Camera",FlightLayer,-1);
            var canvas=new GameObject("InspectionComposite",typeof(Canvas));canvas.transform.SetParent(transform,false);
            var c=canvas.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=main;c.planeDistance=1;c.sortingOrder=1;
            deviceImage=Image(canvas.transform,"InspectionImage");
            var mask=new GameObject("RecoveryClip",typeof(RectTransform),typeof(RectMask2D));mask.transform.SetParent(canvas.transform,false);flightMask=mask.GetComponent<RectTransform>();
            flightImage=Image(mask.transform,"RecoveryImage");
        }
        private Camera MakeCamera(string name,int layer,int depth)
        {
            var go=new GameObject(name);go.transform.SetParent(transform,false);
            var c=go.AddComponent<Camera>();c.orthographic=true;c.depth=main.depth+depth;c.cullingMask=1<<layer;
            c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=Color.clear;c.allowHDR=false;c.nearClipPlane=.03f;c.farClipPlane=100;
            c.GetUniversalAdditionalCameraData().renderPostProcessing=false;return c;
        }
        private RawImage Image(Transform parent,string name)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(RawImage));go.transform.SetParent(parent,false);
            var image=go.GetComponent<RawImage>();image.raycastTarget=false;
            image.rectTransform.anchorMin=Vector2.zero;image.rectTransform.anchorMax=Vector2.one;
            image.rectTransform.offsetMin=image.rectTransform.offsetMax=Vector2.zero;return image;
        }
        public static void SetLayer(Transform root,int layer)
        {
            if(root.gameObject.layer==TargetLayer)return;root.gameObject.layer=layer;foreach(Transform child in root)SetLayer(child,layer);
        }
        public void Refresh(Rect region, Vector3 focus, Vector2 center, float zoom,
            bool recovering, float progress=0)
        {
            Region=region;
            deviceImage.rectTransform.anchorMin=Region.min;deviceImage.rectTransform.anchorMax=Region.max;
            float expand=recovering?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.45f,.75f,progress)):1;
            var low=Region.min;
            var high=new Vector2(Region.xMax,Mathf.Lerp(Region.yMax,RepairHud.HeaderBottom,expand));
            var size=high-low;
            flightMask.anchorMin=low;flightMask.anchorMax=high;flightMask.offsetMin=flightMask.offsetMax=Vector2.zero;
            flightImage.rectTransform.anchorMin=new Vector2(-low.x/size.x,-low.y/size.y);
            flightImage.rectTransform.anchorMax=new Vector2((1-low.x)/size.x,(1-low.y)/size.y);
            Configure(Screen.width,Screen.height);
            var rotation=main.transform.rotation;
            float viewSize=main.orthographicSize/zoom;
            Vector2 offset=Region.center-center;
            var cameraOffset=new Vector3(offset.x*2*viewSize*main.aspect,offset.y*2*viewSize,-main.transform.position.magnitude);
            deviceCamera.transform.SetPositionAndRotation(focus+rotation*cameraOffset,rotation);
            deviceCamera.orthographicSize=viewSize*Region.height;
            flightCamera.transform.SetPositionAndRotation(main.transform.position-main.transform.forward*25,main.transform.rotation);flightCamera.orthographicSize=main.orthographicSize;
            flightCamera.enabled=flightImage.enabled=recovering;
        }
        public object CameraRotation => new {x=deviceCamera.transform.rotation.x,y=deviceCamera.transform.rotation.y,
            z=deviceCamera.transform.rotation.z,w=deviceCamera.transform.rotation.w};
        public Vector3 Forward => deviceCamera.transform.forward;
        public Vector3 Right => deviceCamera.transform.right;
        public Quaternion FlightRotation => main.transform.rotation*Quaternion.Inverse(deviceCamera.transform.rotation);
        public Vector3 Project(Vector3 world)
        {
            var p=deviceCamera.WorldToViewportPoint(world);
            return new Vector3(Region.xMin+p.x*Region.width,Region.yMin+p.y*Region.height,p.z);
        }
        public Ray ScreenRay(Vector2 point)
        {
            return deviceCamera.ViewportPointToRay(new Vector3(
                (point.x/Screen.width-Region.xMin)/Region.width,(point.y/Screen.height-Region.yMin)/Region.height));
        }
        public Vector3 FlightPosition(Vector3 world)
        {
            var p=Project(world);
            return main.ViewportToWorldPoint(p);
        }
        private void Configure(int w,int h)
        {
            if(width==w&&height==h&&Mathf.Approximately(allocatedHeight,Region.height)&&deviceTexture!=null)return;
            width=w;height=h;allocatedHeight=Region.height;Release();
            deviceTexture=Texture(w,Mathf.Max(16,Mathf.RoundToInt(h*Region.height)),"InspectionTarget");
            flightTexture=Texture(w,h,"RecoveryTarget");
            deviceCamera.targetTexture=deviceTexture;flightCamera.targetTexture=flightTexture;
            deviceCamera.aspect=(float)w/(h*Region.height)*Region.width;
            flightCamera.aspect=(float)w/h;
            deviceImage.texture=deviceTexture;flightImage.texture=flightTexture;
        }
        private RenderTexture Texture(int w,int h,string name)
        {
            var rt=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);rt.name=name;rt.antiAliasing=2;rt.Create();return rt;
        }
        // 编辑器合成截图与实际显示使用相同的两路来源，不用单独相机截图冒充完整画面。
        public void RenderSources(int w,int h)
        {
            Configure(w,h);deviceCamera.Render();if(flightCamera.enabled)flightCamera.Render();
        }
        public void RestoreSources() { Configure(Screen.width,Screen.height); }
        private void Release()
        {
            if(deviceTexture!=null){deviceTexture.Release();Destroy(deviceTexture);}
            if(flightTexture!=null){flightTexture.Release();Destroy(flightTexture);}
        }
        private void OnDestroy(){Release();}
    }
}
