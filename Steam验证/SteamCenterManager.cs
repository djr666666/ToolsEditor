using BBG.Network;
using Protocol;
using Steamworks;
using System;
using System.Collections;
using System.Text;
using UnityEngine;



public class SteamCenterManager : MonoSingleton<SteamCenterManager>
{

    //从项目直接copy进来的做了 跟本地一个区分模式
    public enum LoginMode
    {
        Normal,
        Steam,
    }
    public enum GameState
    {
        Initing,//初始化中
        Inverificationing,//验证中
        InverificationComplete,//验证完成
        Exiting,//退出
    }



    public class SteamAuthResponse
    {
        public int code;
        public string message;
        public SteamAuthData data;
    }

    [System.Serializable]
    public class SteamAuthData
    {
        public int id;
        public string steamid;
        public int appid;
        public bool authenticated;
        public string encrypted_token;
        public string @params; // 使用 @ 因为 params 是 C# 关键字
    }

    public LoginMode curMode = LoginMode.Normal; //模式
    private GameState curGameState = GameState.Initing;


    private bool m_bInitialized = false; //初始化
    private HAuthTicket currentTicket = HAuthTicket.Invalid; //初始化无效票据
    private Callback<GetAuthSessionTicketResponse_t> m_GetAuthSessionTicketResponse; //监听


    public bool debugMode = true;//调试
    public float requestTimeout = 10f;
    //public string gameSever = $"ws://-----------/ws"; //后端服务器网址
    public string backendBaseUrl = "http://106.14.73.35:8080";
    public string authenticateEndpoint = "/api/steam/user-auth/authenticate-ticket"; //STEAM

    public class AuthTicketRequest
    {
        public uint appid;      // 你的App ID: 4118470
        public string ticket;   // Base64编码的票据
    }

    public event Action OnStartGame;

    private bool isSteamLoading = false;
    public void SetIsSteamLoading(bool type) => this.isSteamLoading = type;
    public bool GetIsSteamLoading() => this.isActiveAndEnabled;




    public void Awake()
    {
        Debug.Log($"steam  登录系统运行模式：{curMode}");
        DontDestroyOnLoad(gameObject);

        InitializeSteam();
    }
    void InitializeSteam()
    {
        curGameState = GameState.Initing;

        if (curMode != LoginMode.Steam)
        {
            Debug.Log($"未启用 steam 模式 : {curMode}");
            return;
        }
        try
        {
            // 先检查 Steam 客户端是否运行
            if (!SteamAPI.IsSteamRunning())
            {
                Debug.LogError("Steam 客户端未运行！请先启动 Steam");
                return;
            }
            // 测试 Steamworks.NET
            if (!Packsize.Test())
            {
                Debug.LogError("[Packsize] Steamworks.NET 包大小测试失败");
                return;
            }
            if (!DllCheck.Test())
            {
                Debug.LogError("[DllCheck] Steamworks.NET DLL 检查失败");
                return;
            }

            // 初始化 SteamAPI
            m_bInitialized = SteamAPI.Init();
            if (!m_bInitialized)
            {
                Debug.LogError("SteamAPI.Init() 失败！");

                // 检查 steam_appid.txt
                if (!System.IO.File.Exists("steam_appid.txt"))
                {
                    Debug.LogError("未找到 steam_appid.txt 文件！");
                }
                return;
            }
            // 检查Steamworks.NET版本
            Debug.Log($"Steamworks.NET版本: {Steamworks.Version.SteamworksNETVersion}");
            Debug.Log($"<color=yellow> SteamAPI 初始化成功！用户: {SteamFriends.GetPersonaName()}, ID: {SteamUser.GetSteamID()}</color>");

         
            // 重要：在初始化成功后立即创建回调
            CreateCallbacks();

  

            var token = PlayerPrefs.GetString("steam_encrypted_token");
            if (!string.IsNullOrEmpty(token))
            {
                StartGame();
                return;
            }
            StartCoroutine(DelayedGetTicket());       
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Steam 初始化异常: {e.Message}\n{e.StackTrace}");
        }
    }


    IEnumerator DelayedGetTicket()
    {
        yield return null;
        GetSteamTicket();
    }

    void GetSteamTicket()
    {

        curGameState = GameState.Inverificationing;

        if (!m_bInitialized)
        {
            Debug.LogError("Steam 未初始化，无法获取票据");
            return;
        }

        // 如果已有票据，先取消
        if (currentTicket != HAuthTicket.Invalid)
        {
            SteamUser.CancelAuthTicket(currentTicket);
            currentTicket = HAuthTicket.Invalid;
        }

        try
        {
            Debug.Log(" steam 正在获取Steam认证票据...");

            // 准备缓冲区
            byte[] ticketBuffer = new byte[1024];
            uint ticketSize = 0;

            //创建 SteamNetworkingIdentity 对象
            SteamNetworkingIdentity identity = new SteamNetworkingIdentity();
            // 设置身份为当前Steam用户
            identity.SetSteamID(SteamUser.GetSteamID());

            currentTicket = SteamUser.GetAuthSessionTicket(
                ticketBuffer,
                ticketBuffer.Length,
                out ticketSize,
                ref identity
            );

            Debug.Log($"GetAuthSessionTicket 调用完成，句柄: {currentTicket}");
            if (currentTicket == HAuthTicket.Invalid)
            {
                Debug.LogError("steam 获取票据失败：返回无效句柄");
                return;
            }

            // 保存票据数据
            if (ticketSize > 0)
            {
                
                byte[] ticketData = new byte[ticketSize];
                Array.Copy(ticketBuffer, ticketData, ticketSize);
                string base64Ticket = Convert.ToBase64String(ticketData);

                // 临时存储到PlayerPrefs等待回调
                PlayerPrefs.SetString("steam_ticket_buffer", base64Ticket);
                PlayerPrefs.SetInt("steam_ticket_size", (int)ticketSize);
                PlayerPrefs.Save();

                Debug.Log($"steam 票据已保存，大小: {ticketSize} 字节，句柄: {currentTicket}");
            }

        }
        catch (System.Exception e)
        {
            Debug.LogError($"steam 获取票据异常: {e.Message}\n{e.StackTrace}");
        }
    }



    void Update()
    {
        // 关键：必须每帧调用 RunCallbacks
        if (m_bInitialized)
        {
            SteamAPI.RunCallbacks();
        }
    }


    void CreateCallbacks()
    {
        if (!m_bInitialized) return;

        // 销毁旧的回调（如果有）
        if (m_GetAuthSessionTicketResponse != null)
        {
            m_GetAuthSessionTicketResponse.Unregister();
            m_GetAuthSessionTicketResponse = null;
        }

        // 创建新的回调 - 使用 Create 而不是 CreateGameObject
        m_GetAuthSessionTicketResponse = Callback<GetAuthSessionTicketResponse_t>.Create(OnGetAuthSessionTicketResponse);

        Debug.Log("Steam 回调已创建");
    }







    private void OnGetAuthSessionTicketResponse(GetAuthSessionTicketResponse_t param)
    {
        Debug.Log($"steam=== 票据回调触发 ===");
        Debug.Log($"steam结果: {param.m_eResult}");
        Debug.Log($"steam票据句柄: {param.m_hAuthTicket}");
        Debug.Log($"steam请求的句柄: {currentTicket}");

        // 验证票据句柄是否匹配
        if (param.m_hAuthTicket != currentTicket)
        {
            Debug.LogWarning($"steam 票据句柄不匹配: 当前 {currentTicket}, 回调 {param.m_hAuthTicket}");
            return;
        }

        if (param.m_eResult == EResult.k_EResultOK)
        {
            // 从 PlayerPrefs 获取之前保存的票据数据
            string base64Ticket = PlayerPrefs.GetString("steam_ticket_buffer");
            int ticketSize = PlayerPrefs.GetInt("steam_ticket_size");

            if (string.IsNullOrEmpty(base64Ticket) || ticketSize <= 0)
            {
                Debug.LogError("steam 票据数据丢失，请重新获取");
                return;
            }

            // 发送到服务器验证
            Debug.Log($"steam 票据数据有效，大小: {ticketSize}");
            ProcessTicketData(base64Ticket, ticketSize);
        }
        else
        {
            Debug.LogError($"steam 票据验证失败: {param.m_eResult}");
        }
    }



    void ProcessTicketData(string base64Ticket, int ticketSize)
    {
        try
        {
            byte[] ticketData = Convert.FromBase64String(base64Ticket);

            // 转换为十六进制
            StringBuilder hexBuilder = new StringBuilder(ticketSize * 2);
            for (int i = 0; i < ticketSize; i++)
            {
                hexBuilder.AppendFormat("{0:X2}", ticketData[i]);
            }
            string ticketHex = hexBuilder.ToString();

            // 获取其他验证信息
            ulong steamId = SteamUser.GetSteamID().m_SteamID;
            uint appId = SteamUtils.GetAppID().m_AppId;

            Debug.Log($"<color=yellow> 票据处理成功！</color>");
            Debug.Log($"Steam ID: {steamId}");
            Debug.Log($"steam App ID: {appId}");
            Debug.Log($"steam 票据大小: {ticketSize}");
            Debug.Log($"steam 票据预览: {ticketHex.Substring(0, Math.Min(32, ticketHex.Length))}...");

            // 验证票据是否有效
            if (ValidateTicketLocally(ticketData, ticketSize))
            {
                Debug.Log("steam 本地票据验证通过");
            }

            // 发送到服务器
            // 处理票据数据
            if (ticketSize > 0)
            {
                // 3. 构建请求数据
                var requestData = new AuthTicketRequest
                {
                    appid = appId,
                    ticket = ticketHex
                };
                // 发送到服务器验证
                StartCoroutine(SendToServer(requestData));
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"steam 处理票据数据异常: {e.Message}\n{e.StackTrace}");
        }
    }

    IEnumerator SendToServer(AuthTicketRequest requestData)
    {
        string fullUrl = $"{backendBaseUrl}{authenticateEndpoint}";
        // 序列化为JSON
        string json = JsonUtility.ToJson(requestData);
        // 验证 JSON 格式
        ValidateJson(json, requestData.ticket);



        using (UnityEngine.Networking.UnityWebRequest request = new UnityEngine.Networking.UnityWebRequest(fullUrl, "POST"))
        {
            // 设置请求数据
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(jsonBytes);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();

            // 明确指定 UTF-8
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            request.SetRequestHeader("Accept", "application/json");
            Debug.Log($"发送 Steam 票据验证请求...");


            // 发送请求
            request.timeout = (int)requestTimeout;

            yield return request.SendWebRequest();

            // 处理响应
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
            
                string responseText = request.downloadHandler.text;
                Debug.Log($"steam 后端验证成功!  后端响应: {responseText}");

          
                var response = JsonUtility.FromJson<SteamAuthResponse>(responseText);
                if (!string.IsNullOrEmpty(response.data.encrypted_token))
                {
                    Debug.Log($"Token 提取成功: {response.data.encrypted_token}");
                    PlayerPrefs.SetString("steam_encrypted_token", response.data.encrypted_token);
                    PlayerPrefs.Save();
                }


                // 处理成功响应
                StartGame();

            }
            else
            {
                Debug.LogError($"steam 验证失败: {request.error}");
                Debug.LogError($"steam 响应: {request.downloadHandler.text}");

                // 验证失败，可能票据已过期
         
                GetSteamTicket(); // 重新获取票据
            }
        }
    }



    void StartGame()
    {
        curGameState = GameState.InverificationComplete;
        Debug.Log("<color=yellow>steam 进入游戏...</color>");
        SetIsSteamLoading(true);
        OnStartGame?.Invoke();  
    }



    bool ValidateTicketLocally(byte[] ticketData, int ticketSize)
    {
        // 基本验证：检查票据大小和内容
        if (ticketSize < 100 || ticketSize > 1024)
        {
            Debug.LogWarning($"票据大小异常: {ticketSize}");
            return false;
        }

        // 检查是否都是 0（常见错误）
        bool allZero = true;
        for (int i = 0; i < Math.Min(32, ticketSize); i++)
        {
            if (ticketData[i] != 0)
            {
                allZero = false;
                break;
            }
        }

        if (allZero)
        {
            Debug.LogError("票据数据全为0，获取失败");
            return false;
        }

        return true;
    }

    void ValidateJson(string json, string originalBase64)
    {
        try
        {
            Debug.Log("🔍 验证JSON格式...");

            // 1. 检查是否包含必要字段
            if (!json.Contains("\"appid\""))
                Debug.LogError("❌ JSON 缺少 appid 字段");
            else
                Debug.Log("✅ JSON 包含 appid 字段");

            if (!json.Contains("\"ticket\""))
                Debug.LogError("❌ JSON 缺少 ticket 字段");
            else
                Debug.Log("✅ JSON 包含 ticket 字段");

            // 2. 检查 appid 格式
            if (json.Contains("4118470.0"))
                Debug.LogError("❌ appid 被序列化为浮点数");
            else if (json.Contains("\"4118470\""))
                Debug.LogError("❌ appid 被序列化为字符串");
            else if (json.Contains("4118470"))
                Debug.Log("✅ appid 格式正确（整数）");

            // 3. 检查 ticket 是否被截断或修改
            AuthTicketRequest parsed = JsonUtility.FromJson<AuthTicketRequest>(json);
            if (parsed.ticket == originalBase64)
                Debug.Log("✅ ticket 在JSON中未改变");
            else
            {
                Debug.LogError("❌ ticket 被修改了！");
                Debug.LogError($"原始: {originalBase64.Substring(0, 50)}...");
                Debug.LogError($"JSON中: {parsed.ticket.Substring(0, 50)}...");
            }

            // 4. 检查 Base64 能否解码
            try
            {
                byte[] decoded = Convert.FromBase64String(parsed.ticket);
                Debug.Log($"✅ Base64 可解码: {decoded.Length} 字节");
            }
            catch (FormatException)
            {
                Debug.LogError("❌ ticket 不是有效的 Base64");
                Debug.LogError($"问题字符: {FindInvalidBase64Char(parsed.ticket)}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"验证JSON失败: {e.Message}");
        }
    }
    string FindInvalidBase64Char(string base64)
    {
        foreach (char c in base64)
        {
            if (!char.IsLetterOrDigit(c) && c != '+' && c != '/' && c != '=')
            {
                return $"'{c}' (0x{(int)c:X2})";
            }
        }
        return "未找到无效字符";
    }

    public void OnStartLoading()
    {
        var data = new EncryptedLoginReq();
        data.EncryptedData = PlayerPrefs.GetString("steam_encrypted_token");

        NetworkManager.Instance.SendMessage(data, MessageType.MsgTypeEncryptedLogin);
        UIManager.UIP.ShowMask();
    }

}
