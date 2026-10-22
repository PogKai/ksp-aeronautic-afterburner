using System;
using UnityEngine;
public static class NozzleBakeProbe
{
 public static void Run()
 {
  int checks=0;
  foreach (Vector3 scale in new[] {Vector3.one*0.5f,Vector3.one,Vector3.one*2f,new Vector3(0.5f,0.52f,0.5f)})
  foreach (float opening in new[] {0.6f,1f})
  {
   var root=new GameObject("ScaleProbe"); var child=new GameObject("Skin"); child.transform.parent=root.transform;
   var bone=new GameObject("Bone"); bone.transform.parent=root.transform;
   var mesh=new Mesh(); mesh.vertices=new[]{new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(-1,0,0)};
   mesh.triangles=new[]{0,1,2}; mesh.bindposes=new[]{bone.transform.worldToLocalMatrix*child.transform.localToWorldMatrix};
   mesh.boneWeights=new[]{new BoneWeight{boneIndex0=0,weight0=1},new BoneWeight{boneIndex0=0,weight0=1},new BoneWeight{boneIndex0=0,weight0=1}};
   var skin=child.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh=mesh; skin.bones=new[]{bone.transform}; skin.rootBone=bone.transform;
   root.transform.localScale=scale; root.transform.position=new Vector3(3,4,5); root.transform.rotation=Quaternion.Euler(21,37,13); bone.transform.localScale=Vector3.one*opening;
   var baked=new Mesh(); skin.BakeMesh(baked);
   for(int i=0;i<mesh.vertices.Length;i++)
   {
    Vector3 expected=bone.transform.TransformPoint(mesh.vertices[i]);
    Vector3 corrected=child.transform.position+child.transform.rotation*baked.vertices[i];
    if((expected-corrected).magnitude>0.0001f) throw new Exception("Scaled skin fit mismatch "+scale+"/"+opening);
    checks++;
   }
   UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(baked);
  }
  Debug.Log("[NozzleBakeProbe] PASS "+checks+" animated scaled vertices, including rotated nonuniform model");
 }
}

