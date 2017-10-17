﻿using Microsoft.Toolkit.ThreeD;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Microsoft.Toolkit.ThreeD
{
    /// <summary>
    /// WebRTC server behavior that enables 3dtoolkit webrtc
    /// </summary>
    /// <remarks>
    /// Adding this component requires an <c>nvEncConfig.json</c> and <c>webrtcConfig.json</c> in the run directory
    /// To see debug information from this component, add a <see cref="WebRTCServerDebug"/> to the same object
    /// </remarks>
    [RequireComponent(typeof(WebRTCEyes))]
    public class WebRTCServer : MonoBehaviour
    { 
        /// <summary>
        /// A mutable peers list that we'll keep updated, and derive connect/disconnect operations from
        /// </summary>
        public PeerListState PeerList = null;

        /// <summary>
        /// Instance that represents the underlying native plugin that powers the webrtc experience
        /// </summary>
        public StreamingUnityServerPlugin Plugin = null;

        /// <summary>
        /// Internal reference to the eyes component
        /// </summary>
        /// <remarks>
        /// Since <see cref="WebRTCEyes"/> is a required component, we just set this value in <see cref="Awake"/>
        /// </remarks>
        private WebRTCEyes Eyes = null;

        /// <summary>
        /// The location to be placed at as determined by client control
        /// </summary>
        private Vector3 location = Vector3.zero;

        /// <summary>
        /// The location to look at as determined by client control
        /// </summary>
        private Vector3 lookAt = Vector3.zero;

        /// <summary>
        /// The upward vector as determined by client control
        /// </summary>
        private Vector3 up = Vector3.zero;

        /// <summary>
        /// Internal flag used to indicate we are shutting down
        /// </summary>
        private bool isClosing = false;

        /// <summary>
        /// Internal reference to the previous peer
        /// </summary>
        private PeerListState.Peer previousPeer = null;

        /// <summary>
        /// Unity engine object Awake() hook
        /// </summary>
        private void Awake()
        {
            // make sure that the render window continues to render when the game window does not have focus
            Application.runInBackground = true;

            // capture the attached eyes into a member variable
            Eyes = this.GetComponent<WebRTCEyes>();

            // open the connection
            Open();
        }

        /// <summary>
        /// Unity engine object OnDestroy() hook
        /// </summary>
        private void OnDestroy()
        {
            // close the connection
            Close();
        }

        /// <summary>
        /// Unity engine object Update() hook
        /// </summary>
        private void Update()
        {
            if (!isClosing)
            {
                transform.position = location;
                transform.LookAt(lookAt, up);
            }
            
            // encode the entire render texture at the end of the frame
            StartCoroutine(Plugin.EncodeAndTransmitFrame());
            
            // check if we need to connect to a peer, and if so, do it
            if ((previousPeer == null && PeerList.SelectedPeer != null) ||
                (previousPeer != null && PeerList.SelectedPeer != null && !previousPeer.Equals(PeerList.SelectedPeer)))
            {
                Debug.Log("imma connect to " + PeerList.SelectedPeer.Id);
                Plugin.ConnectToPeer(PeerList.SelectedPeer.Id);
                previousPeer = PeerList.SelectedPeer;
            }
        }

        /// <summary>
        /// Opens the webrtc server and gets things rolling
        /// </summary>
        public void Open()
        {
            if (Plugin != null)
            {
                Close();
            }

            // clear the underlying mutable peer data
            PeerList.Peers.Clear();
            PeerList.SelectedPeer = null;

            // Create the plugin
            Plugin = new StreamingUnityServerPlugin();

            // hook it's input event
            // note: if you wish to capture debug data, see the <see cref="StreamingUnityServerDebug"/> behaviour
            Plugin.Input += OnInputData;

            Plugin.PeerConnect += (int peerId, string peerName) =>
            {
                PeerList.Peers.Add(new PeerListState.Peer()
                {
                    Id = peerId,
                    Name = peerName
                });
            };

            Plugin.PeerDisconnect += (int peerId) =>
            {
                // TODO(bengreenier): this can be optimized to stop at the first match
                PeerList.Peers.RemoveAll(p => p.Id == peerId);
            };
        }

        /// <summary>
        /// Closes the webrtc server and shuts things down
        /// </summary>
        public void Close()
        {
            if (!isClosing && Plugin != null)
            {
                Plugin.Dispose();
                Plugin = null;
            }
        }

        /// <summary>
        /// Handles input data and updates local transformations
        /// </summary>
        /// <param name="data">input data</param>
        private void OnInputData(string data)
        {
            try
            {
                var node = SimpleJSON.JSON.Parse(data);
                string messageType = node["type"];

                Debug.Log(data);

                switch (messageType)
                {
                    case "stereo-rendering":
                        var isStereo = node["body"].IsBoolean ? node["body"].AsBool : false;

                        // change the number of eyes so that we can react
                        // TODO(bengreenier): rendering behavior should change as a result
                        if (isStereo)
                        {
                            this.Eyes.TotalEyes = WebRTCEyes.EyeCount.Two;
                        }
                        else
                        {
                            this.Eyes.TotalEyes = WebRTCEyes.EyeCount.One;
                        }

                        break;

                    case "keyboard-event":
                        var kbBody = node["body"];
                        int kbMsg = kbBody["msg"];
                        int kbWParam = kbBody["wParam"];

                        break;

                    case "mouse-event":
                        var mouseBody = node["body"];
                        int mouseMsg = mouseBody["msg"];
                        int mouseWParam = mouseBody["wParam"];
                        int mouseLParam = mouseBody["lParam"];

                        break;

                    case "camera-transform-lookat":
                        string cam = node["body"];
                        if (cam != null && cam.Length > 0)
                        {
                            string[] sp = cam.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

                            Vector3 loc = new Vector3();
                            loc.x = float.Parse(sp[0]);
                            loc.y = float.Parse(sp[1]);
                            loc.z = float.Parse(sp[2]);
                            location = loc;

                            Vector3 look = new Vector3();
                            look.x = float.Parse(sp[3]);
                            look.y = float.Parse(sp[4]);
                            look.z = float.Parse(sp[5]);
                            lookAt = look;

                            Vector3 u = new Vector3();
                            u.x = float.Parse(sp[6]);
                            u.y = float.Parse(sp[7]);
                            u.z = float.Parse(sp[8]);
                            up = u;
                        }

                        break;

                    case "camera-transform-stereo":

                        string camera = node["body"];
                        if (camera != null && camera.Length > 0)
                        {
                            var leftViewProjection = new Matrix4x4();
                            var rightViewProjection = new Matrix4x4();

                            var coords = camera.Split(',');
                            var index = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                for (int j = 0; j < 4; j++)
                                {
                                    // We are receiving 32 values for the left/right matrix. The first 16 are for left followed by the right matrix.
                                    rightViewProjection[i, j] = float.Parse(coords[16+index]);
                                    leftViewProjection[i, j] = float.Parse(coords[index++]);
                                }
                            }

                            // This needs to run on the main thread
                            UIThreadSingleton.Dispatch(() =>
                            {
                                this.Eyes.EyeOne.cullingMatrix = leftViewProjection;
                                this.Eyes.EyeTwo.cullingMatrix = rightViewProjection;
                            });
                        }
                        
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("InputUpdate threw " + ex.Message + "\r\n" + ex.StackTrace);
            }
        }
    }
}