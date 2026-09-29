using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using System;
using System.Collections;

namespace CatchTheLizard
{
    public sealed class NetworkGameManager : NetworkBehaviour
    {
        public static NetworkGameManager Instance { get; private set; }
        public readonly NetworkVariable<bool> TeamWon = new(false);
        [SerializeField] ushort port = 7777;
        [SerializeField] string defaultAddress = "127.0.0.1";
        string address;
        GUIStyle titleStyle;
        GUIStyle centerStyle;

        void Awake()
        {
            Instance = this;
            address = defaultAddress;
            NetworkManager manager = FindAnyObjectByType<NetworkManager>();
            if (manager != null) manager.ConnectionApprovalCallback = ApproveConnection;
        }

        IEnumerator Start()
        {
            yield return null;
            string[] args = Environment.GetCommandLineArgs();
            if (Array.Exists(args, arg => arg == "-autohost") && !NetworkManager.Singleton.IsListening)
            {
                ConfigureTransport("0.0.0.0");
                NetworkManager.Singleton.StartHost();
            }
            else if (Array.Exists(args, arg => arg == "-autojoin") && !NetworkManager.Singleton.IsListening)
            {
                ConfigureTransport(defaultAddress);
                NetworkManager.Singleton.StartClient();
            }
            if (Array.Exists(args, arg => arg == "-smoketest"))
            {
                yield return new WaitForSeconds(5f);
                Debug.Log($"CTL_SMOKE_OK listening={NetworkManager.Singleton.IsListening} players={NetworkManager.Singleton.ConnectedClientsList.Count}");
#if !UNITY_EDITOR
                Application.Quit();
#endif
            }
        }

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = NetworkManager.Singleton.ConnectedClientsIds.Count < 4;
            response.CreatePlayerObject = response.Approved;
            response.Pending = false;
            response.Reason = response.Approved ? string.Empty : "This prototype supports up to four players.";
        }

        void Update()
        {
            if (!IsServer || TeamWon.Value) return;
            ExitArea exit = FindFirstObjectByType<ExitArea>();
            if (exit == null) return;
            foreach (ContainerTool container in FindObjectsByType<ContainerTool>(FindObjectsSortMode.None))
            {
                if (!container.ContainsLizard.Value || !container.IsHeld) continue;
                NetworkPlayer carrier = NetworkPlayer.FindByClientId(container.HolderClientId.Value);
                if (carrier != null && exit.Contains(carrier.transform.position))
                {
                    TeamWon.Value = true;
                    break;
                }
            }
        }

        void OnGUI()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            centerStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;

            if (!manager.IsListening)
            {
                GUILayout.BeginArea(new Rect(Screen.width / 2f - 190, Screen.height / 2f - 155, 380, 310), GUI.skin.box);
                GUILayout.Label("CATCH THE LIZARD", titleStyle, GUILayout.Height(55));
                GUILayout.Label("LAN prototype · 2–4 players", centerStyle);
                GUILayout.Space(20);
                GUILayout.Label("Host address (clients enter the host's LAN IP):");
                address = GUILayout.TextField(address, 64, GUILayout.Height(30));
                GUILayout.Space(10);
                if (GUILayout.Button("HOST GAME", GUILayout.Height(45)))
                {
                    ConfigureTransport("0.0.0.0");
                    manager.StartHost();
                }
                if (GUILayout.Button("JOIN GAME", GUILayout.Height(45)))
                {
                    ConfigureTransport(string.IsNullOrWhiteSpace(address) ? defaultAddress : address.Trim());
                    manager.StartClient();
                }
                GUILayout.Space(10);
                GUILayout.Label("WASD move · Mouse look · Shift sprint · Ctrl crouch\nE pick up · Q/R drop · LMB/RMB use · Esc cursor", centerStyle);
                GUILayout.EndArea();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            GUI.Box(new Rect(12, 12, 410, 78), $"{(manager.IsHost ? "HOST" : "CLIENT")}  •  Players: {manager.ConnectedClientsList.Count}\nE pickup | Q/R drop | LMB/RMB use\nBroom herds • Spray stuns • Container catches");
            if (TeamWon.Value)
            {
                GUI.Box(new Rect(Screen.width / 2f - 230, Screen.height / 2f - 95, 460, 190), "");
                GUI.Label(new Rect(Screen.width / 2f - 220, Screen.height / 2f - 70, 440, 55), "TEAM SUCCESS!", titleStyle);
                GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height / 2f - 12, 400, 30), "The lizard made it outside safely.", centerStyle);
                if (GUI.Button(new Rect(Screen.width / 2f - 90, Screen.height / 2f + 35, 180, 42), "PLAY AGAIN"))
                    RequestRestartServerRpc();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void ConfigureTransport(string host)
        {
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetConnectionData(host, port, "0.0.0.0");
        }

        [ServerRpc(RequireOwnership = false)]
        void RequestRestartServerRpc()
        {
            TeamWon.Value = false;
            LizardController lizard = FindFirstObjectByType<LizardController>();
            if (lizard != null) lizard.ServerReset();
            foreach (HoldableItem item in FindObjectsByType<HoldableItem>(FindObjectsSortMode.None)) item.ServerReset();
            foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None)) player.ServerResetPosition();
        }
    }
}
