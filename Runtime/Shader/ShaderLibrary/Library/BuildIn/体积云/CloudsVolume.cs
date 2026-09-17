using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF.RunTime.ShaderCtr
{
    [ExecuteInEditMode]
    public class CloudsVolume : MonoBehaviour
    {
        [Space(10)]
        public int volumeSamples = 50;
        public float volumeSize = 500f;
        float volumeOffset;

        [Space(10)]
        public Mesh Coludmesh;
        public Material cloudsMaterial;

        private Matrix4x4 matrix;
        private Matrix4x4[] matrices;


        // Update is called once per frame
        void Update()
        {
            cloudsMaterial.SetFloat("_cloudsPosition", transform.position.y);
            cloudsMaterial.SetFloat("_cloudsHeight", volumeSize);

            volumeOffset = volumeSize / volumeSamples / 2f;
            Vector3 startPosition = transform.position + (Vector3.up * (volumeOffset * volumeSamples / 2f));
            matrices = new Matrix4x4[volumeSamples];
            for (int i = 0; i < volumeSamples; i++)
            {
                matrix = Matrix4x4.TRS(startPosition - (Vector3.up * volumeOffset * i), transform.rotation, transform.localScale);
                matrices[i] = matrix;
                Graphics.DrawMesh(Coludmesh, matrix, cloudsMaterial, 0);
            }
            Graphics.DrawMeshInstanced(Coludmesh, 0, cloudsMaterial, matrices, volumeSamples);
        }
    }
}
