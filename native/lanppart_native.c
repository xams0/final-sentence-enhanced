// Lanppart native helpers for Final Sentence (TheEndClient-Win64-Shipping.exe, UE 5.5.1, repo rev 18880).
//
// The shipped client is built without engine server code: UWorld::Listen() is compiled to `return false`.
// This DLL restores a listen server by redirecting LoadMap's call to the stub towards an implementation
// that mirrors UE 5.5.1 UWorld::Listen. It is loaded from Lua with package.loadlib(); exported entry points
// receive a lua_State* they ignore (UE4SS does not export the Lua C API) and return 0 results.
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <stdarg.h>

// ---- RVAs for the supported build (verified against expected bytes before use) ----
#define RVA_GENGINE                0x6ADC828  // UEngine* GEngine
#define RVA_FNAME_FROM_ENAME       0x0FD5B40  // const FNameEntryId* (void* scratch, EName)
#define RVA_CREATE_NAMED_NETDRIVER_LOCAL 0x3B78090  // bool CreateNamedNetDriver_Local(UEngine*, FWorldContext&, FName, FName)
#define RVA_GC_BARRIER             0x1114E50  // incremental GC write barrier for TObjectPtr
#define RVA_GC_BARRIER_FLAG        0x6947091  // byte: barrier enabled
#define RVA_LISTEN_STUB            0x0E236C0  // UWorld::Listen stub: xor al,al; ret; int3 x13
#define RVA_LOADMAP_LISTEN_CALL    0x3B92316  // call UWorld::Listen inside UEngine::LoadMap

#define RVA_WORLD_INTERNALGETNETMODE 0x3C0EA40  // ENetMode UWorld::InternalGetNetMode()
#define RVA_ACTOR_GETNETMODE       0x30E2470  // ENetMode AActor::GetNetMode()
#define RVA_RS_GET_BRIDGE          0x1BE1A40  // UReplicationBridge* UReplicationSystem::GetReplicationBridge()
#define RVA_BRIDGE_GET_ACTOR_HANDLE 0x1B9AF40 // FNetRefHandle bridge GetReplicatedRefHandle(UObject*, flags)
#define RVA_RS_SET_FILTER          0x1BEBB40  // bool UReplicationSystem::SetFilter(FNetRefHandle, FNetObjectFilterHandle, FName)
#define RVA_RS_GET_OWNING_CONN     0x1BE1900  // uint32 UReplicationSystem::GetOwningNetConnection(FNetRefHandle)
#define RVA_ACTOR_UPDATE_OWNING_CONN 0x30F2730 // AActor::UpdateOwningNetConnection()
#define RVA_RPC_OWNER_CHECK_JE     0x1B93B92  // Iris RPC receive: je <allowed> after owner == connection
#define RVA_RPC_OWNER_CHECK_OK     0x1B93CE5
#define RVA_LOBBY_START_MATCHMAKING_CALL 0x436DA70  // lobby lambda: Backend->StartMatchmaking(...)
#define RVA_BACKEND_START_MATCHMAKING 0x42EB1C0     // UPGBackendSubsystem::StartMatchmaking
#define RVA_TICKFLUSH_IRIS_CHECKS  0x3774FB1  // UNetDriver::TickFlush: client-only Iris update conditions

// Final Sentence gameplay (APGGameplayGameState / APGGameplayPlayerState)
#define RVA_DUEL_ROULETTE_CALL     0x42CDE64  // HandleActivePlayerCarriageChanged: StartRoulette(loser)
#define RVA_ROUND_TIMEOUT_CALL     0x42EC0BE  // GameState tick: Execute(PS, delay, RoundTimeout)
#define RVA_START_ROULETTE         0x42D9AC0  // GameState: StartRoulette(PS, delay, duration)
#define RVA_EXECUTE_WRITER         0x42E65E0  // GameState: Execute(PS, delay, EPGPlayerResult)
#define RVA_ADVANCE_ROUND          0x42E62C0  // GameState: set BetweenRounds and next round timing
#define RVA_MOVE_CARRIAGE          0x42F8FE0  // PlayerState: move carriage by delta, clamped to target
#define RVA_LOAD_CHAMBER           0x42F8AB0  // PlayerState: LoadRevolverChamberAtSelectedIndex(bSpin)
#define RVA_SEND_RECOVER_RPC       0x42A5840  // PlayerController: Client_RecoverAfterRoulette(carriage)
#define RVA_GAMEPLAYPC_STATICCLASS 0x42A3FF0  // APGGameplayPlayerController::StaticClass

#define OFF_UOBJECT_CLASS          0x10
#define OFF_USTRUCT_INHERIT_CHAIN  0x30
#define OFF_USTRUCT_INHERIT_DEPTH  0x38
#define OFF_ACTOR_OWNER            0x158
#define OFF_ACTOR_ROLE             0x168
#define OFF_PLAYERSTATE_FLAGS      0x2B2
#define PLAYERSTATE_FLAG_BOT       0x08
#define OFF_GS_TEXTLENSPERROUND    0x450
#define OFF_GS_OPTIONS             0x4E8  // FPGGameplayOptions
#define OPT_TYPEMODE               0x01
#define OPT_PLAYROULETTE           0x2F
#define OPT_ISPRIVATE              0x00
#define OPT_BOTSCOUNT              0x28
#define TYPEMODE_BattleRoyal       0
#define TYPEMODE_Duel              2
#define OFF_PS_WRITERID            0x490
#define OFF_PS_WRITERSTATE         0x494
#define OFF_PS_CARRIAGE            0x498
#define OFF_PS_ROUND               0x51C
#define OFF_PS_DRUM_MASK           0x528
#define OFF_PS_DRUM_SELECTED       0x529
#define OFF_PS_DRUM_CHAMBERS       0x52A
#define RVA_CVAR_USE_IRIS          0x698FAF0  // int32 backing net.Iris.UseIrisReplication
#define RVA_MODULEMANAGER_GET      0x0FB5270  // FModuleManager& FModuleManager::Get()
#define RVA_MODULE_IS_LOADED       0x0FB5D00  // bool FModuleManager::IsModuleLoaded(FName)
#define RVA_MODULE_LOAD            0x0FB5E10  // IModuleInterface* FModuleManager::LoadModuleWithFailureReason(FName, EModuleLoadResult&, ELoadModuleFlags)
#define RVA_FNAME_FROM_ANSI        0x0FCF9A0  // FName::FName(const ANSICHAR*, EFindName)

#define NAME_GameNetDriver 0x11A
#define OFF_ENGINE_NETDRIVERDEFS   0x1098  // TArray<FNetDriverDefinition>
#define SIZEOF_NETDRIVERDEF        0x28
#define OFF_NETDRIVERDEF_USEIRIS   0x20

#define VT_NETDRIVER_INITLISTEN 0x2E8
#define VT_NETDRIVER_SETWORLD   0x400

#define OFF_WORLD_NETWORKNOTIFY     0x28
#define OFF_WORLD_NETDRIVER         0x38
#define OFF_WORLD_GAMESTATE         0x160
#define OFF_WORLD_LEVELCOLLECTIONS  0x188
#define SIZEOF_LEVELCOLLECTION      0x78
#define OFF_LEVELCOLLECTION_NETDRIVER 0x10
#define OFF_ENGINE_WORLDLIST        0x10F8  // TIndirectArray<FWorldContext>
#define OFF_CONTEXT_WORLD           0x2C0
#define OFF_CONTEXT_ACTIVENETDRIVERS 0x210  // TArray<FNamedNetDriver> (stride 0x10, NetDriver first)
#define SIZEOF_NAMEDNETDRIVER       0x10
#define OFF_NETDRIVER_NAME          0x200
#define OFF_NETDRIVER_SERVERCONNECTION 0xF0
#define OFF_NETDRIVER_CLIENTCONNECTIONS 0xF8
#define OFF_NETDRIVER_REPLICATIONSYSTEM 0x810
#define OFF_PLAYER_PLAYERCONTROLLER 0x30
#define OFF_WORLD_GAMEINSTANCE     0x1D8
#define OFF_GAMEINSTANCE_LOCALPLAYERS 0x38
#define OFF_NETCONNECTION_PARENTCONNID 0x1D2C
#define OFF_NETDRIVER_MAXINTERNETCLIENTRATE 0xC4
#define OFF_NETDRIVER_MAXCLIENTRATE 0xC8

enum { DynamicSourceLevels = 0, DynamicDuplicatedLevels = 1, StaticLevels = 2 };

typedef struct { uint32_t ComparisonIndex; uint32_t Number; } FName;
typedef struct { wchar_t* Data; int32_t Num; int32_t Max; } FString;
typedef struct { uint8_t* Data; int32_t Num; int32_t Max; } TArrayRaw;

typedef const uint32_t* (*FNameFromEName_t)(void* scratch, uint32_t ename);
typedef char (*CreateNamedNetDriverLocal_t)(void* engine, void* context, FName name, FName def);
typedef void (*GCBarrier_t)(void* obj);
typedef char (*InitListen_t)(void* netDriver, void* notify, void* url, char reuse, FString* error);
typedef void (*SetWorld_t)(void* netDriver, void* world);

static uint8_t* g_base;
static int g_installed;

// Path of a file next to this DLL: the mod's own folder, never the game's folder.
static void ModFile(char* path, const char* name) {
    HMODULE self = NULL;
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       (LPCSTR)(void*)&ModFile, &self);
    GetModuleFileNameA(self, path, MAX_PATH);
    char* slash = strrchr(path, '\\');
    if (slash) snprintf(slash + 1, MAX_PATH - (size_t)(slash + 1 - path), "%s", name);
}

static void Log(const char* fmt, ...) {
    char path[MAX_PATH];
    ModFile(path, "lanppart_native.log");
    FILE* f = fopen(path, "a");
    if (!f) return;
    SYSTEMTIME t;
    GetLocalTime(&t);
    fprintf(f, "[%02d:%02d:%02d.%03d] ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
    va_list ap;
    va_start(ap, fmt);
    vfprintf(f, fmt, ap);
    va_end(ap);
    fputc('\n', f);
    fclose(f);
}

#define AT(rva) ((void*)(g_base + (rva)))

static void* VFunc(void* obj, size_t off) { return (*(void***)obj)[off / sizeof(void*)]; }

static void StoreObjPtr(void** slot, void* value) {
    if (*(uint8_t*)AT(RVA_GC_BARRIER_FLAG) && value) ((GCBarrier_t)AT(RVA_GC_BARRIER))(value);
    *slot = value;
}

static FName NameFromEName(uint32_t ename) {
    uint8_t scratch[16];
    FName n = { *((FNameFromEName_t)AT(RVA_FNAME_FROM_ENAME))(scratch, ename), 0 };
    return n;
}

// UEngine::GetWorldContextFromWorld
static uint8_t* WorldContextOf(void* engine, void* world) {
    TArrayRaw* list = (TArrayRaw*)((uint8_t*)engine + OFF_ENGINE_WORLDLIST);
    for (int i = 0; i < list->Num; i++) {
        uint8_t* ctx = ((uint8_t**)list->Data)[i];
        if (ctx && *(void**)(ctx + OFF_CONTEXT_WORLD) == world) return ctx;
    }
    return NULL;
}

// FindNamedNetDriver_Local
static void* FindNetDriver(uint8_t* ctx, FName name) {
    TArrayRaw* drivers = (TArrayRaw*)(ctx + OFF_CONTEXT_ACTIVENETDRIVERS);
    for (int i = 0; i < drivers->Num; i++) {
        void* d = *(void**)(drivers->Data + (size_t)i * SIZEOF_NAMEDNETDRIVER);
        if (d && memcmp((uint8_t*)d + OFF_NETDRIVER_NAME, &name, sizeof(FName)) == 0) return d;
    }
    return NULL;
}

static void SetCollectionsNetDriver(uint8_t* world, void* netDriver) {
    TArrayRaw* cols = (TArrayRaw*)(world + OFF_WORLD_LEVELCOLLECTIONS);
    for (int i = 0; i < cols->Num; i++) {
        uint8_t* col = cols->Data + (size_t)i * SIZEOF_LEVELCOLLECTION;
        if (col[0] == DynamicSourceLevels || col[0] == StaticLevels)
            StoreObjPtr((void**)(col + OFF_LEVELCOLLECTION_NETDRIVER), netDriver);
    }
}

// Mirrors UE 5.5.1 UWorld::Listen (WITH_SERVER_CODE branch).
static char LanppartListen(uint8_t* world, void* url) {
    void* engine = *(void**)AT(RVA_GENGINE);
    void** worldNetDriver = (void**)(world + OFF_WORLD_NETDRIVER);
    Log("Listen requested: world=%p engine=%p", world, engine);
    if (*worldNetDriver) {
        Log("Listen aborted: world already has a net driver");
        return 0;
    }
    uint8_t* ctx = WorldContextOf(engine, world);
    if (!ctx) {
        Log("Listen aborted: no world context for this world");
        return 0;
    }
    FName gameNetDriver = NameFromEName(NAME_GameNetDriver);
    if (((CreateNamedNetDriverLocal_t)AT(RVA_CREATE_NAMED_NETDRIVER_LOCAL))(engine, ctx, gameNetDriver, gameNetDriver)) {
        void* netDriver = FindNetDriver(ctx, gameNetDriver);
        StoreObjPtr(worldNetDriver, netDriver);
        if (netDriver) {
            ((SetWorld_t)VFunc(netDriver, VT_NETDRIVER_SETWORLD))(netDriver, world);
            SetCollectionsNetDriver(world, netDriver);
        }
    }
    void* netDriver = *worldNetDriver;
    if (!netDriver) {
        Log("Listen failed: could not create GameNetDriver");
        return 0;
    }
    FString error = { 0 };
    char ok = ((InitListen_t)VFunc(netDriver, VT_NETDRIVER_INITLISTEN))(netDriver, world + OFF_WORLD_NETWORKNOTIFY, url, 0, &error);
    if (!ok) {
        Log("Listen failed: InitListen error '%ls'", error.Data ? error.Data : L"");
        ((SetWorld_t)VFunc(netDriver, VT_NETDRIVER_SETWORLD))(netDriver, NULL);
        SetCollectionsNetDriver(world, NULL);
        *worldNetDriver = NULL;
        return 0;
    }
    int32_t* maxInternet = (int32_t*)((uint8_t*)netDriver + OFF_NETDRIVER_MAXINTERNETCLIENTRATE);
    int32_t* maxClient = (int32_t*)((uint8_t*)netDriver + OFF_NETDRIVER_MAXCLIENTRATE);
    if (*maxInternet < *maxClient && *maxInternet > 2500) *maxClient = *maxInternet;
    Log("Listen OK: netDriver=%p", netDriver);
    return 1;
}

static int WriteCode(void* dst, const void* src, size_t len) {
    DWORD old;
    if (!VirtualProtect(dst, len, PAGE_EXECUTE_READWRITE, &old)) return 0;
    memcpy(dst, src, len);
    VirtualProtect(dst, len, old, &old);
    FlushInstructionCache(GetCurrentProcess(), dst, len);
    return 1;
}

// Executable memory within rel32 range of the game module, for small stubs.
static uint8_t* AllocNear(size_t size) {
    SYSTEM_INFO si;
    GetSystemInfo(&si);
    uintptr_t gran = si.dwAllocationGranularity;
    uintptr_t base = (uintptr_t)g_base & ~(gran - 1);
    for (uintptr_t d = gran; d < 0x70000000; d += gran) {
        void* p = VirtualAlloc((void*)(base - d), size, MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE);
        if (p) return (uint8_t*)p;
    }
    return NULL;
}

// UWorld::InternalGetNetMode is compiled as `if (NetDriver) return NM_Client;` because IsRunningClientOnly()
// is constant-true in this build. Send it to a stub returning NM_ListenServer when the driver is a server.
static int PatchNetMode(void) {
    static const uint8_t expected[] = {
        0x48, 0x83, 0x79, 0x38, 0x00,  // cmp qword ptr [rcx+38h], 0
        0x48, 0x8B, 0xD9,              // mov rbx, rcx
        0x74, 0x0E,                    // je  +0Eh
        0xB8, 0x03, 0x00, 0x00, 0x00,  // mov eax, 3   <- patched
    };
    uint8_t* fn = AT(RVA_WORLD_INTERNALGETNETMODE + 9);
    if (memcmp(fn, expected, sizeof(expected)) != 0) {
        Log("NetMode patch refused: unexpected bytes");
        return 0;
    }
    static const uint8_t stubCode[] = {
        0x48, 0x8B, 0x43, 0x38,                          // mov rax, [rbx+38h]      ; World->NetDriver
        0x48, 0x83, 0xB8, 0xF0, 0x00, 0x00, 0x00, 0x01,  // cmp qword ptr [rax+0F0h], 1 ; ServerConnection
        0xB8, 0x03, 0x00, 0x00, 0x00,                    // mov eax, 3 (NM_Client)
        0x83, 0xD8, 0x00,                                // sbb eax, 0 -> 2 (NM_ListenServer) when no ServerConnection
        0x48, 0x81, 0xC4, 0x90, 0x00, 0x00, 0x00,        // add rsp, 90h
        0x5B,                                            // pop rbx
        0xC3,                                            // ret
    };
    uint8_t* stub = AllocNear(sizeof(stubCode));
    if (!stub) {
        Log("NetMode patch failed: no memory near module");
        return 0;
    }
    memcpy(stub, stubCode, sizeof(stubCode));
    uint8_t* site = fn + 10;
    uint8_t jmp[5] = { 0xE9 };
    int32_t rel = (int32_t)(stub - (site + 5));
    memcpy(jmp + 1, &rel, 4);
    if (!WriteCode(site, jmp, sizeof(jmp))) return 0;
    Log("NetMode patched: stub=%p", stub);
    return 1;
}

// Final Sentence's own gameplay code only runs its self-contained flow (options from URL, text, bots,
// manual OnRep calls for the local writer) when AActor::GetNetMode() == NM_Standalone; dedicated servers
// get those from GameLift instead. A listen-server host needs the standalone flow, so the game module's
// calls to GetNetMode see NM_Standalone while the engine still sees NM_ListenServer.
static const uint32_t kGameNetModeCalls[] = {
    0x42BF070,  // APGGameplayGameMode::BeginPlay
    0x42BFB87,  // APGGameplayGameState::CheckActivePlayerInput
    0x42CD5B9,  // APGGameplayGameState (local OnRep helper)
    0x42CE3D4,  // APGGameplayGameMode::HandleActivePlayerArrayChanged
    0x42CEDAB,  // APGGameplayGameMode::InitGame
    0x42E5912,  // APGGameplayPlayerState (local OnRep helper)
    0x42E8BA6,  // APGGameplayGameMode::SetSessionStateToIntro
    0x42E9387,  // APGGameplayGameMode::SetSessionStateToGameplay
};

static int PatchGameNetModeCalls(void) {
    uint8_t* getNetMode = AT(RVA_ACTOR_GETNETMODE);
    uint8_t* stub = AllocNear(64);
    if (!stub) return 0;
    uint8_t code[] = {
        0x48, 0x83, 0xEC, 0x28,                    // sub rsp, 28h
        0xFF, 0x15, 0x0C, 0x00, 0x00, 0x00,        // call [rip+0Ch] -> AActor::GetNetMode
        0x48, 0x83, 0xC4, 0x28,                    // add rsp, 28h
        0x83, 0xF8, 0x02,                          // cmp eax, 2 (NM_ListenServer)
        0x75, 0x02,                                // jne +2
        0x31, 0xC0,                                // xor eax, eax (NM_Standalone)
        0xC3,                                      // ret
        0, 0, 0, 0, 0, 0, 0, 0,                    // absolute address of GetNetMode
    };
    memcpy(code + 22, &getNetMode, 8);
    memcpy(stub, code, sizeof(code));
    int patched = 0;
    for (size_t i = 0; i < sizeof(kGameNetModeCalls) / sizeof(kGameNetModeCalls[0]); i++) {
        uint8_t* call = AT(kGameNetModeCalls[i]);
        if (call[0] != 0xE8 || call + 5 + *(int32_t*)(call + 1) != getNetMode) {
            Log("GetNetMode call %u: unexpected bytes, skipped", (unsigned)i);
            continue;
        }
        int32_t rel = (int32_t)(stub - (call + 5));
        if (WriteCode(call + 1, &rel, 4)) patched++;
    }
    Log("GetNetMode calls redirected: %d", patched);
    return patched == (int)(sizeof(kGameNetModeCalls) / sizeof(kGameNetModeCalls[0]));
}

// ---- Lanppart lives (every mode) and K.O. roulette ----
// Lives mode is set by the Lua host (lanppart_lives_on) for sessions whose lobby has the roulette
// unchecked. The session itself runs with roulette ON, so every roulette trigger of the game still fires
// (Duel round loser, Battle Royale mistakes limit); each trigger, and each execution that would end a
// writer early (round timeout, K.O. knockdown), costs a life instead. Lives = revolver chambers
// (BulletSlotsNum, no initial bullet): a lost life loads one chamber without spinning, so the replicated
// drum shows the lives lost. When the drum is full, the warden executes the writer.
// Without lives, K.O. sessions with the roulette checked spin the roulette instead of knocking out.
typedef void (*StartRoulette_t)(void* gs, void* ps, float delay, float duration);
typedef void (*ExecuteWriter_t)(void* gs, void* ps, float delay, uint8_t result);
typedef void (*AdvanceRound_t)(void* gs, void* ps, char fromRoulette);
typedef void (*MoveCarriage_t)(void* ps, int32_t delta, int32_t target);
typedef void (*LoadChamber_t)(void* ps, char spin);
typedef void (*SendRecoverRPC_t)(void* pc, int32_t carriage);
typedef void (*BotJumpTo_t)(void* typist, int32_t carriage, int32_t target);
typedef void (*PsVoid_t)(void* ps);
typedef void (*SetWriterState_t)(void* ps, uint8_t state);
typedef void* (*StaticClass_t)(void);
typedef uint8_t* (*GetFlowSettings_t)(void);
typedef void (*InitFlowSettings_t)(void* holder);
typedef double (*GetServerTime_t)(void* gs);

#define RVA_BR_ROULETTE_CALL       0x42E575E  // mistakes limit reached: StartRoulette(PS)
#define RVA_BR_SKIP_ROULETTE_CALL  0x42E5774  // ... too close to the round end: Execute(PS, SkipRouletteToExecute)
#define RVA_BR_SKIP_REWIND_CALL    0x42E578B  // rewind would pass the round end: Execute(PS, SkipRewindToExecute)
#define RVA_KO_KNOCKDOWN_CALL      0x42CDFA3  // K.O. round end: Execute(last writer, Knockdown)
#define RVA_RESET_ROUND_MISTAKES   0x42FA980  // PlayerState: clear the round's typing/mistake state
#define RVA_SET_WRITER_STATE       0x42FC0E0  // PlayerState: SetWriterState(EPGWriterState)
#define RVA_BOTPC_STATICCLASS      0x4287BF0  // bot controller StaticClass
#define RVA_BOT_JUMP_TO            0x42F9070  // bot typist component: jump to carriage
#define RVA_GET_FLOW_SETTINGS      0x42921A0  // settings holder (+0x110: UPGGameFlowSettings*)
#define RVA_INIT_FLOW_SETTINGS     0x10E2970

#define OFF_GS_PLAYERS             0x470  // TArray<APGGameplayPlayerState*>
#define OFF_FLOWHOLDER_SETTINGS    0x110
#define OFF_FLOW_ROULETTE_DURATION 0x2E0
#define OFF_PS_ROULETTE_END        0x534
#define OFF_BOTPC_TYPIST           0x860
#define VT_ACTOR_FORCENETUPDATE    0x6D8
#define VT_GS_GETSERVERTIME        0x730
#define TYPEMODE_Knockdown         1
#define ROLE_Authority 3
#define WS_Typing   2
#define WS_Await    4
#define WS_Roulette 6
#define WS_Dead     8
#define RESULT_RoundTimeout          2
#define RESULT_SkipRouletteToExecute 4
#define RESULT_SkipRewindToExecute   5
#define RESULT_Knockdown             7

static volatile int g_livesMode = 0;
static volatile int g_serverMode = 0;  // dedicated server instance (V2)

static int IsA(uint8_t* obj, uint8_t* cls) {
    if (!obj || !cls) return 0;
    uint8_t* objCls = *(uint8_t**)(obj + OFF_UOBJECT_CLASS);
    int32_t depth = *(int32_t*)(cls + OFF_USTRUCT_INHERIT_DEPTH);
    return depth <= *(int32_t*)(objCls + OFF_USTRUCT_INHERIT_DEPTH) &&
           (*(uint8_t***)(objCls + OFF_USTRUCT_INHERIT_CHAIN))[depth] == cls + OFF_USTRUCT_INHERIT_CHAIN;
}

static int IsServer(uint8_t* gs) { return gs && gs[OFF_ACTOR_ROLE] == ROLE_Authority; }
static int LivesActive(uint8_t* gs) { return g_livesMode && IsServer(gs); }
static uint8_t TypeMode(uint8_t* gs) { return gs[OFF_GS_OPTIONS + OPT_TYPEMODE]; }

static int RoundEndCarriage(uint8_t* gs, uint8_t* ps) {
    int32_t round = *(int32_t*)(ps + OFF_PS_ROUND);
    TArrayRaw* lens = (TArrayRaw*)(gs + OFF_GS_TEXTLENSPERROUND);
    if (round < 0 || round >= lens->Num) return 0;
    return ((int32_t*)lens->Data)[round];
}

static float RouletteDuration(void) {
    uint8_t* holder = ((GetFlowSettings_t)AT(RVA_GET_FLOW_SETTINGS))();
    if (!*(uint8_t**)(holder + OFF_FLOWHOLDER_SETTINGS)) ((InitFlowSettings_t)AT(RVA_INIT_FLOW_SETTINGS))(holder);
    return *(float*)(*(uint8_t**)(holder + OFF_FLOWHOLDER_SETTINGS) + OFF_FLOW_ROULETTE_DURATION);
}

static void ForceNetUpdate(uint8_t* actor) {
    ((PsVoid_t)(*(void***)actor)[VT_ACTOR_FORCENETUPDATE / 8])(actor);
}

// Skips the rest of the writer's round, as the game does for a duelist surviving the roulette.
static void SkipRound(uint8_t* gs, uint8_t* ps) {
    // (The game only advances a writer in the roulette state when told it comes from the roulette.)
    ((AdvanceRound_t)AT(RVA_ADVANCE_ROUND))(gs, ps, ps[OFF_PS_WRITERSTATE] == WS_Roulette);
    int32_t roundEnd = RoundEndCarriage(gs, ps);
    int32_t carriage = *(int32_t*)(ps + OFF_PS_CARRIAGE);
    uint8_t* owner = *(uint8_t**)(ps + OFF_ACTOR_OWNER);
    if (!(ps[OFF_PLAYERSTATE_FLAGS] & PLAYERSTATE_FLAG_BOT)) {
        if (IsA(owner, ((StaticClass_t)AT(RVA_GAMEPLAYPC_STATICCLASS))()))
            ((SendRecoverRPC_t)AT(RVA_SEND_RECOVER_RPC))(owner, roundEnd);
    } else if (IsA(owner, ((StaticClass_t)AT(RVA_BOTPC_STATICCLASS))())) {
        ((BotJumpTo_t)AT(RVA_BOT_JUMP_TO))(*(void**)(owner + OFF_BOTPC_TYPIST), roundEnd, roundEnd);
    }
    if (carriage < roundEnd)
        ((MoveCarriage_t)AT(RVA_MOVE_CARRIAGE))(ps, roundEnd - carriage, roundEnd);
}

// Lets the writer go on typing the current round, as the game does after a Battle Royale roulette.
// A remote writer's game blocked its keyboard when it reached the mistakes limit, and only unblocks it on
// a replicated writer state change: Typing right away would not replicate (no change), so the writer
// waits (Await) for a moment and lanppart_server_tick puts them back to Typing.
#define CONTINUE_DELAY 0.8
#define MAX_PENDING_CONTINUES 64
static struct { uint8_t* gs; uint8_t* ps; double due; } g_continues[MAX_PENDING_CONTINUES];
static int g_continueCount;

static double ServerTime(uint8_t* gs) {
    return ((GetServerTime_t)(*(void***)gs)[VT_GS_GETSERVERTIME / 8])(gs);
}

static void ContinueRound(uint8_t* gs, uint8_t* ps) {
    ((PsVoid_t)AT(RVA_RESET_ROUND_MISTAKES))(ps);
    ((SetWriterState_t)AT(RVA_SET_WRITER_STATE))(ps, WS_Await);
    ForceNetUpdate(ps);
    if (g_continueCount < MAX_PENDING_CONTINUES) {
        g_continues[g_continueCount].gs = gs;
        g_continues[g_continueCount].ps = ps;
        g_continues[g_continueCount].due = ServerTime(gs) + CONTINUE_DELAY;
        g_continueCount++;
    } else {
        ((SetWriterState_t)AT(RVA_SET_WRITER_STATE))(ps, WS_Typing);
    }
}

static int HasPlayer(uint8_t* gs, uint8_t* ps) {
    TArrayRaw* players = (TArrayRaw*)(gs + OFF_GS_PLAYERS);
    for (int32_t i = 0; i < players->Num; i++)
        if (((uint8_t**)players->Data)[i] == ps) return 1;
    return 0;
}

static void ResumeWriters(uint8_t* gs) {
    double now = ServerTime(gs);
    for (int i = 0; i < g_continueCount; ) {
        if (g_continues[i].gs != gs) { i++; continue; }
        uint8_t* ps = g_continues[i].ps;
        if (now < g_continues[i].due) { i++; continue; }
        if (HasPlayer(gs, ps) && ps[OFF_PS_WRITERSTATE] == WS_Await) {
            ((SetWriterState_t)AT(RVA_SET_WRITER_STATE))(ps, WS_Typing);
            ForceNetUpdate(ps);
        }
        g_continues[i] = g_continues[--g_continueCount];
    }
}

// Costs the writer a life; executes them (with deathResult) when it was their last one.
static void LoseLife(uint8_t* gs, uint8_t* ps, float delay, uint8_t deathResult, int skipRound) {
    if (!ps || ps[OFF_PS_WRITERSTATE] == WS_Dead) return;
    uint8_t* mask = ps + OFF_PS_DRUM_MASK;
    uint8_t chambers = ps[OFF_PS_DRUM_CHAMBERS];
    for (uint8_t i = 0; i < chambers; i++) {
        if (!(*mask & (1u << i))) {
            ps[OFF_PS_DRUM_SELECTED] = i;
            ((LoadChamber_t)AT(RVA_LOAD_CHAMBER))(ps, 0);
            break;
        }
    }
    int lost = 0;
    for (uint8_t i = 0; i < chambers; i++) lost += (*mask >> i) & 1;
    Log("Lives: writer %d lost a life (mode %d), lives %d/%d", *(int32_t*)(ps + OFF_PS_WRITERID), TypeMode(gs),
        chambers - lost, chambers);
    if (lost >= chambers) {
        ((ExecuteWriter_t)AT(RVA_EXECUTE_WRITER))(gs, ps, delay, deathResult);
        return;
    }
    if (skipRound) SkipRound(gs, ps);
    else ContinueRound(gs, ps);
}

static void Execute(uint8_t* gs, uint8_t* ps, float delay, uint8_t result) {
    ((ExecuteWriter_t)AT(RVA_EXECUTE_WRITER))(gs, ps, delay, result);
}

// Duel: the slower writer of a round plays the roulette.
static void OnDuelRoulette(uint8_t* gs, uint8_t* ps, float delay, float duration) {
    if (!LivesActive(gs)) { ((StartRoulette_t)AT(RVA_START_ROULETTE))(gs, ps, delay, duration); return; }
    LoseLife(gs, ps, delay, RESULT_SkipRouletteToExecute, 1);
}

// Battle Royale: the writer reached the mistakes limit of the round.
static void OnMistakesRoulette(uint8_t* gs, uint8_t* ps, float delay, float duration) {
    if (!LivesActive(gs)) { ((StartRoulette_t)AT(RVA_START_ROULETTE))(gs, ps, delay, duration); return; }
    LoseLife(gs, ps, delay, RESULT_SkipRouletteToExecute, 0);
}

// Battle Royale: mistakes limit reached too close to the round end for a roulette.
static void OnMistakesExecute(uint8_t* gs, uint8_t* ps, float delay, uint8_t result) {
    if (!LivesActive(gs)) { Execute(gs, ps, delay, result); return; }
    LoseLife(gs, ps, delay, result, 0);
}

// A rewind or the round timer would end the writer's round too late.
static void OnRoundLost(uint8_t* gs, uint8_t* ps, float delay, uint8_t result) {
    if (!LivesActive(gs) || (result != RESULT_RoundTimeout && result != RESULT_SkipRewindToExecute)) {
        Execute(gs, ps, delay, result);
        return;
    }
    LoseLife(gs, ps, delay, result, 1);
}

// K.O.: the last writer of a round is knocked out.
static void OnKnockdown(uint8_t* gs, uint8_t* ps, float delay, uint8_t result) {
    if (LivesActive(gs)) { LoseLife(gs, ps, delay, result, 1); return; }
    if (IsServer(gs) && gs[OFF_GS_OPTIONS + OPT_PLAYROULETTE]) {
        ((StartRoulette_t)AT(RVA_START_ROULETTE))(gs, ps, delay, RouletteDuration());
        return;
    }
    Execute(gs, ps, delay, result);
}

// The game resolves a surviving roulette only for Battle Royale and Duel writers; a K.O. survivor would
// stay in the roulette forever. Called periodically by the Lua host: let them skip the round.
static void ResolveKnockdownRoulettes(uint8_t* gs) {
    if (!IsServer(gs) || TypeMode(gs) != TYPEMODE_Knockdown) return;
    double now = ((GetServerTime_t)(*(void***)gs)[VT_GS_GETSERVERTIME / 8])(gs);
    TArrayRaw* players = (TArrayRaw*)(gs + OFF_GS_PLAYERS);
    for (int32_t i = 0; i < players->Num; i++) {
        uint8_t* ps = ((uint8_t**)players->Data)[i];
        if (!ps || ps[OFF_PS_WRITERSTATE] != WS_Roulette || now < *(float*)(ps + OFF_PS_ROULETTE_END)) continue;
        uint8_t sel = ps[OFF_PS_DRUM_SELECTED];
        if (sel < ps[OFF_PS_DRUM_CHAMBERS] && (ps[OFF_PS_DRUM_MASK] >> sel) & 1) continue;  // shot: the game handles it
        Log("K.O.: writer %d survived the roulette", *(int32_t*)(ps + OFF_PS_WRITERID));
        SkipRound(gs, ps);
    }
}

// Points a `call rel32` or tail `jmp rel32` (verified to target `expected`) at `handler` through a near
// absolute-jump stub.
static int RedirectCall(uint32_t callRva, uint32_t expectedRva, void* handler, const char* what) {
    uint8_t* call = AT(callRva);
    if ((call[0] != 0xE8 && call[0] != 0xE9) || call + 5 + *(int32_t*)(call + 1) != AT(expectedRva)) {
        Log("%s: refused, unexpected bytes", what);
        return 0;
    }
    uint8_t* stub = AllocNear(16);
    if (!stub) return 0;
    uint8_t jmp[12] = { 0x48, 0xB8 };  // mov rax, imm64; jmp rax
    uint64_t target = (uint64_t)(uintptr_t)handler;
    memcpy(jmp + 2, &target, 8);
    jmp[10] = 0xFF;
    jmp[11] = 0xE0;
    memcpy(stub, jmp, sizeof(jmp));
    int32_t rel = (int32_t)(stub - (call + 5));
    if (!WriteCode(call + 1, &rel, 4)) return 0;
    Log("%s: installed", what);
    return 1;
}

// ---- Bots ----
// When the waiting time ends with free slots left, the game mode adds "mandatory" bots, naming them from the
// start of its bot name list. On official servers every expected player is known in advance; here a player
// may still be connecting when all the scheduled bots are in, and reusing a taken name is a fatal error.
// Mandatory bots take the names not used yet instead, and are dropped when none is left.
typedef void (*AddBot_t)(void* gm, int32_t nameIndex);
#define RVA_MANDATORY_ADDBOT_CALL  0x42BC405  // CheckMinPlayersToStart lambda: AddBot(gm, i)
#define RVA_ADD_BOT                0x42BD400
#define OFF_GM_BOTS_TO_SPAWN       0x3E0
#define OFF_GM_BOT_NAMES           0x3E8  // TArray<FString>

#define OFF_GM_GAMESTATE           0x3F8  // GameplayGameState

// Bots take the bot names in order, so the next unused name is the number of bots in the session.
static int32_t BotsPresent(uint8_t* gm) {
    uint8_t* gs = *(uint8_t**)(gm + OFF_GM_GAMESTATE);
    if (!gs) return 0;
    TArrayRaw* players = (TArrayRaw*)(gs + OFF_GS_PLAYERS);
    int32_t bots = 0;
    for (int32_t i = 0; i < players->Num; i++) {
        uint8_t* ps = ((uint8_t**)players->Data)[i];
        if (ps && (ps[OFF_PLAYERSTATE_FLAGS] & PLAYERSTATE_FLAG_BOT)) bots++;
    }
    return bots;
}

// Adds a bot under the next unused bot name (0 when none is left).
static int AddNextBot(uint8_t* gm) {
    int32_t next = BotsPresent(gm);
    if (next >= ((TArrayRaw*)(gm + OFF_GM_BOT_NAMES))->Num) {
        Log("Bots: bot dropped (no unused name)");
        return 0;
    }
    ((AddBot_t)AT(RVA_ADD_BOT))(gm, next);  // (AddBot decrements the game mode's bots-to-spawn count)
    return 1;
}

static void AddMandatoryBot(uint8_t* gm, int32_t index) {
    (void)index;
    if (g_serverMode) return;
    AddNextBot(gm);
}

// The game mode's BeginPlay adds a few bots right away, then more over the waiting time. A dedicated
// server keeps every place for the players: its bots only come with the organizer's start
// (lanppart_server_start).
#define RVA_PRESPAWN_ADDBOT_CALL   0x42BF18B
static const uint32_t kScheduledAddBotJumps[] = { 0x42AF392, 0x42C3E43 };  // timer lambdas: jmp AddBot
static void GameAddedBot(uint8_t* gm, int32_t index) {
    if (g_serverMode) return;
    ((AddBot_t)AT(RVA_ADD_BOT))(gm, index);
}

// ---- Writers' seats not loaded yet ----
// The game state's GetPlayableWriter(id) reads the player start's writer list without checking that the
// player start exists. In Duel the seats come with a streamed sub-level, which a dedicated server (no
// rendering, fast load) can query before it is in: answer "no seat" instead of crashing.
typedef void* (*GetWriter_t)(void* playerStart, int32_t writerId);
#define RVA_GS_GET_WRITER_JUMP     0x42CCAB7  // mov rcx, [rcx+4A0h] (player start); jmp GetWriter
#define RVA_PLAYERSTART_GET_WRITER 0x42F5E00

static void* GetWriterOrNull(void* playerStart, int32_t writerId) {
    if (!playerStart) return NULL;
    return ((GetWriter_t)AT(RVA_PLAYERSTART_GET_WRITER))(playerStart, writerId);
}

// ---- Client: pawns of bots ----
// APawn::OnRep_Controller retargets the controller's camera manager, which only local controllers have.
// The game's own servers never send other players' controllers; ours do (bot controllers included), and a
// client receiving a bot's pawn before its controller's pawn crashes on the missing camera manager.
// Detour: skip the retarget when the controller has no camera manager.
#define RVA_ONREP_CONTROLLER_CAMERA 0x3871159  // mov rsi, [rbx+360h] (PlayerCameraManager)
#define RVA_ONREP_CONTROLLER_NEXT   0x3871160  // je ... (after the GC barrier flag test)
#define RVA_ONREP_CONTROLLER_SKIP   0x3871188

static int PatchRemoteControllerCamera(void) {
    static const uint8_t expected[] = {
        0x80, 0x3D, 0x38, 0x5F, 0x0D, 0x03, 0x00,  // cmp byte [GC barrier flag], 0
        0x48, 0x8B, 0xB3, 0x60, 0x03, 0x00, 0x00,  // mov rsi, [rbx+360h]
        0x74, 0x08,                                // je
    };
    uint8_t* site = (uint8_t*)AT(RVA_ONREP_CONTROLLER_CAMERA);
    uint8_t* at = site - 7;
    if (memcmp(at, expected, sizeof(expected)) != 0) {
        Log("Remote controller camera patch refused: unexpected bytes");
        return 0;
    }
    uint8_t* stub = AllocNear(64);
    if (!stub) return 0;
    uint8_t code[] = {
        0x48, 0x8B, 0xB3, 0x60, 0x03, 0x00, 0x00,  // mov rsi, [rbx+360h]
        0x48, 0x85, 0xF6,                          // test rsi, rsi
        0x75, 0x0C,                                // jnz cont
        0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,        // mov rax, skip
        0xFF, 0xE0,                                // jmp rax
        0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,        // cont: mov rax, &flag
        0x80, 0x38, 0x00,                          // cmp byte [rax], 0
        0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,        // mov rax, next
        0xFF, 0xE0,                                // jmp rax
    };
    uint64_t skip = (uint64_t)(uintptr_t)AT(RVA_ONREP_CONTROLLER_SKIP);
    uint64_t flag = (uint64_t)(uintptr_t)AT(RVA_GC_BARRIER_FLAG);
    uint64_t next = (uint64_t)(uintptr_t)AT(RVA_ONREP_CONTROLLER_NEXT);
    memcpy(code + 14, &skip, 8);
    memcpy(code + 26, &flag, 8);
    memcpy(code + 39, &next, 8);
    memcpy(stub, code, sizeof(code));
    uint8_t jmp[7] = { 0xE9, 0, 0, 0, 0, 0x90, 0x90 };  // jmp stub; nop; nop (replaces the mov)
    int32_t rel = (int32_t)(stub - (site + 5));
    memcpy(jmp + 1, &rel, 4);
    if (!WriteCode(site, jmp, sizeof(jmp))) return 0;
    Log("Remote controller camera patched");
    return 1;
}

// ---- Dedicated server (V2) ----
// A server instance has no local player: it hosts without playing. Its sessions must wait in
// WaitingForPlayers until the organizer starts them from the launcher, so the game mode's timers that
// start the intro on their own are ignored; the start (lanppart_server_start) adds the bots and calls
// SetSessionStateToIntro itself.
typedef void (*GmVoid_t)(void* gm);
#define RVA_SET_SESSION_INTRO      0x42E8630  // APGGameplayGameMode::SetSessionStateToIntro
static const uint32_t kIntroTimerLeas[] = { 0x42DAAAF, 0x42DB01E, 0x42EA53D };  // lea rax, SetSessionStateToIntro
#define OFF_WORLD_AUTHORITYGAMEMODE 0x158
#define OFF_GS_FREE_WRITER_IDS     0x488  // writer ids left for players and bots

static void IntroTimer(uint8_t* gm) {
    if (g_serverMode) {
        Log("Server: automatic session start ignored (waiting for the organizer)");
        return;
    }
    ((GmVoid_t)AT(RVA_SET_SESSION_INTRO))(gm);
}

// Points a `lea rax, [rip+rel32]` (verified to target `expected`) at `handler` through a near stub.
static int RedirectLea(uint32_t leaRva, uint32_t expectedRva, void* handler, const char* what) {
    uint8_t* lea = AT(leaRva);
    if (lea[0] != 0x48 || lea[1] != 0x8D || lea[2] != 0x05 || lea + 7 + *(int32_t*)(lea + 3) != AT(expectedRva)) {
        Log("%s: refused, unexpected bytes", what);
        return 0;
    }
    uint8_t* stub = AllocNear(16);
    if (!stub) return 0;
    uint8_t jmp[12] = { 0x48, 0xB8 };  // mov rax, imm64; jmp rax
    uint64_t target = (uint64_t)(uintptr_t)handler;
    memcpy(jmp + 2, &target, 8);
    jmp[10] = 0xFF;
    jmp[11] = 0xE0;
    memcpy(stub, jmp, sizeof(jmp));
    int32_t rel = (int32_t)(stub - (lea + 7));
    return WriteCode(lea + 3, &rel, 4);
}

static int PatchServer(void) {
    for (size_t i = 0; i < sizeof(kIntroTimerLeas) / sizeof(kIntroTimerLeas[0]); i++)
        if (!RedirectLea(kIntroTimerLeas[i], RVA_SET_SESSION_INTRO, (void*)&IntroTimer, "Session start timer")) return 0;
    Log("Server: session start timers redirected");
    return 1;
}

static uint8_t* HostedGameMode(void) {
    void* engine = *(void**)AT(RVA_GENGINE);
    TArrayRaw* list = (TArrayRaw*)((uint8_t*)engine + OFF_ENGINE_WORLDLIST);
    for (int i = 0; i < list->Num; i++) {
        uint8_t* ctx = ((uint8_t**)list->Data)[i];
        uint8_t* world = ctx ? *(uint8_t**)(ctx + OFF_CONTEXT_WORLD) : NULL;
        uint8_t* driver = world ? *(uint8_t**)(world + OFF_WORLD_NETDRIVER) : NULL;
        if (!driver || *(void**)(driver + OFF_NETDRIVER_SERVERCONNECTION)) continue;
        return *(uint8_t**)(world + OFF_WORLD_AUTHORITYGAMEMODE);
    }
    return NULL;
}

// Adds the bots the Lua side asked for (game mode BotToSpawnCount), within the free places.
static int SpawnRequestedBots(uint8_t* gm) {
    uint8_t* gs = *(uint8_t**)(gm + OFF_GM_GAMESTATE);
    int spawned = 0;
    while (*(int32_t*)(gm + OFF_GM_BOTS_TO_SPAWN) > 0 && gs && *(int32_t*)(gs + OFF_GS_FREE_WRITER_IDS) > 0) {
        int32_t before = *(int32_t*)(gm + OFF_GM_BOTS_TO_SPAWN);
        if (!AddNextBot(gm) || *(int32_t*)(gm + OFF_GM_BOTS_TO_SPAWN) == before) break;  // refused
        spawned++;
    }
    return spawned;
}

static int PatchLives(void) {
    return RedirectCall(RVA_DUEL_ROULETTE_CALL, RVA_START_ROULETTE, (void*)&OnDuelRoulette, "Duel round loss") &&
           RedirectCall(RVA_BR_ROULETTE_CALL, RVA_START_ROULETTE, (void*)&OnMistakesRoulette, "Mistakes roulette") &&
           RedirectCall(RVA_BR_SKIP_ROULETTE_CALL, RVA_EXECUTE_WRITER, (void*)&OnMistakesExecute, "Mistakes execution") &&
           RedirectCall(RVA_BR_SKIP_REWIND_CALL, RVA_EXECUTE_WRITER, (void*)&OnRoundLost, "Rewind execution") &&
           RedirectCall(RVA_ROUND_TIMEOUT_CALL, RVA_EXECUTE_WRITER, (void*)&OnRoundLost, "Round timeout") &&
           RedirectCall(RVA_KO_KNOCKDOWN_CALL, RVA_EXECUTE_WRITER, (void*)&OnKnockdown, "K.O. knockdown") &&
           RedirectCall(RVA_MANDATORY_ADDBOT_CALL, RVA_ADD_BOT, (void*)&AddMandatoryBot, "Mandatory bots") &&
           RedirectCall(RVA_PRESPAWN_ADDBOT_CALL, RVA_ADD_BOT, (void*)&GameAddedBot, "Pre-spawned bots") &&
           RedirectCall(kScheduledAddBotJumps[0], RVA_ADD_BOT, (void*)&GameAddedBot, "Scheduled bots") &&
           RedirectCall(kScheduledAddBotJumps[1], RVA_ADD_BOT, (void*)&GameAddedBot, "Scheduled bots") &&
           RedirectCall(RVA_GS_GET_WRITER_JUMP, RVA_PLAYERSTART_GET_WRITER, (void*)&GetWriterOrNull, "Writer seats");
}

// UNetDriver::TickFlush only runs the Iris update (UpdateIrisReplicationViews + ReplicationSystem->NetUpdate)
// on clients: the server branch was under WITH_SERVER_CODE. Skip the client-only checks so it runs whenever
// a ReplicationSystem exists; UpdateIrisReplicationViews already handles servers (one view per client).
static int PatchIrisServerUpdate(void) {
    static const uint8_t expected[] = {
        0x48, 0x8B, 0x07,                          // mov rax, [rdi]
        0x48, 0x8B, 0xCF,                          // mov rcx, rdi
        0xFF, 0x90, 0xE8, 0x03, 0x00, 0x00,        // call [rax+3E8h]   ; IsServer()
    };
    uint8_t* at = AT(RVA_TICKFLUSH_IRIS_CHECKS);
    if (memcmp(at, expected, sizeof(expected)) != 0) {
        Log("Iris server update patch refused: unexpected bytes");
        return 0;
    }
    // jmp short to `cmp [rdi+810h], rsi` (ReplicationSystem != null) at +3Ah
    static const uint8_t jmp[] = { 0xEB, 0x38 };
    if (!WriteCode(at, jmp, sizeof(jmp))) return 0;
    Log("Iris server update patched");
    return 1;
}

// Iris rejects client->server RPCs unless the object's owning connection matches the reader's id, and in
// this build the ids never match on a listen server. Players are friends on a private session: accept them.
static int PatchServerRpcAccess(void) {
    uint8_t* at = AT(RVA_RPC_OWNER_CHECK_JE);
    int32_t rel = *(int32_t*)(at + 2);
    if (at[0] != 0x0F || at[1] != 0x84 || at + 6 + rel != (uint8_t*)AT(RVA_RPC_OWNER_CHECK_OK)) {
        Log("Server RPC access patch refused: unexpected bytes");
        return 0;
    }
    uint8_t jmp[6] = { 0xE9, 0, 0, 0, 0, 0x90 };  // jmp rel32; nop
    int32_t newRel = (int32_t)((uint8_t*)AT(RVA_RPC_OWNER_CHECK_OK) - (at + 5));
    memcpy(jmp + 1, &newRel, 4);
    if (!WriteCode(at, jmp, sizeof(jmp))) return 0;
    Log("Server RPC access patched");
    return 1;
}

// ---- Steam lobby: no AWS matchmaking ----
// When every lobby member is ready, each one calls UPGBackendSubsystem::StartMatchmaking. The Lua side
// hosts / joins a self-hosted session instead, so the backend must not be contacted.
// With the mod, every lobby game is hosted by the lobby host, whatever its mode. (Quick games outside a
// lobby still use the official servers: they don't go through this call.)
static void LobbyStartMatchmaking(void* backend, void* ticketId, int32_t playersCount, uint8_t* options,
                                  void* playerIds, void* selfId, char extended) {
    (void)backend; (void)ticketId; (void)playerIds; (void)selfId; (void)extended;
    Log("Lobby start: self-hosted session (mode %d, %d players), backend matchmaking skipped",
        options[OPT_TYPEMODE], playersCount);
}

static int PatchPostLogin(void);

static int Install(void) {
    if (g_installed) return 1;
    g_base = (uint8_t*)GetModuleHandleW(NULL);
    static const uint8_t stub[] = { 0x32, 0xC0, 0xC3, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC };
    uint8_t* stubAt = AT(RVA_LISTEN_STUB);
    uint8_t* call = AT(RVA_LOADMAP_LISTEN_CALL);
    int32_t rel = *(int32_t*)(call + 1);
    if (memcmp(stubAt, stub, sizeof(stub)) != 0 || call[0] != 0xE8 || call + 5 + rel != stubAt) {
        Log("Install refused: unexpected game build (bytes mismatch)");
        return 0;
    }
    // Trampoline in the stub's int3 padding: mov rax, imm64; jmp rax (12 bytes, fits the 13 free bytes).
    uint8_t tramp[12] = { 0x48, 0xB8 };
    uint64_t target = (uint64_t)(uintptr_t)&LanppartListen;
    memcpy(tramp + 2, &target, 8);
    tramp[10] = 0xFF;
    tramp[11] = 0xE0;
    uint8_t* trampAt = stubAt + 3;
    if (!WriteCode(trampAt, tramp, sizeof(tramp))) return 0;
    int32_t newRel = (int32_t)(trampAt - (call + 5));
    if (!WriteCode(call + 1, &newRel, 4)) return 0;
    if (!PatchNetMode()) return 0;
    if (!PatchGameNetModeCalls()) return 0;
    if (!PatchLives()) return 0;
    if (!PatchRemoteControllerCamera()) return 0;
    if (!PatchPostLogin()) return 0;
    if (!PatchIrisServerUpdate()) return 0;
    if (!PatchServerRpcAccess()) return 0;
    if (!RedirectCall(RVA_LOBBY_START_MATCHMAKING_CALL, RVA_BACKEND_START_MATCHMAKING,
                      (void*)&LobbyStartMatchmaking, "Lobby matchmaking")) return 0;
    g_installed = 1;
    Log("Installed: base=%p listen trampoline=%p", g_base, trampAt);
    return 1;
}

// Iris is only used when the driver definition opts in (byte at +0x20, not exposed to config)
// and net.Iris.UseIrisReplication > 0. Both sides of a connection must agree.
typedef void* (*ModuleManagerGet_t)(void);
typedef FName* (*FNameFromAnsi_t)(FName* out, const char* name, int findType);
typedef void* (*LoadModule_t)(void* mm, FName name, int32_t* outResult, uint32_t flags);
typedef char (*IsModuleLoaded_t)(void* mm, FName name);

static void EnableIris(void) {
    if (!g_base) g_base = (uint8_t*)GetModuleHandleW(NULL);
    void* engine = *(void**)AT(RVA_GENGINE);
    *(int32_t*)AT(RVA_CVAR_USE_IRIS) = 1;
    // IrisCore is statically linked but only started when Iris is enabled at boot.
    void* mm = ((ModuleManagerGet_t)AT(RVA_MODULEMANAGER_GET))();
    FName irisCore;
    ((FNameFromAnsi_t)AT(RVA_FNAME_FROM_ANSI))(&irisCore, "IrisCore", 1);
    if (!((IsModuleLoaded_t)AT(RVA_MODULE_IS_LOADED))(mm, irisCore)) {
        int32_t result = -1;
        void* module = ((LoadModule_t)AT(RVA_MODULE_LOAD))(mm, irisCore, &result, 0);
        Log("IrisCore module load: module=%p result=%d", module, result);
    }
    FName gameNetDriver = NameFromEName(NAME_GameNetDriver);
    TArrayRaw* defs = (TArrayRaw*)((uint8_t*)engine + OFF_ENGINE_NETDRIVERDEFS);
    for (int i = 0; i < defs->Num; i++) {
        uint8_t* def = defs->Data + (size_t)i * SIZEOF_NETDRIVERDEF;
        if (memcmp(def, &gameNetDriver, sizeof(FName)) == 0) {
            def[OFF_NETDRIVERDEF_USEIRIS] = 1;
            Log("Iris enabled for GameNetDriver definition");
        }
    }
}

// Dev diagnostics: log each input of UEngine's Iris decision for the GameNetDriver.
typedef char (*WillUseIris_t)(void* engine, void* ctx, FName def, FName name);
typedef char (*GIIrisOverride_t)(void* gi, FName def);
typedef char (*CmdlineIris_t)(void);
static void DebugIris(void) {
    if (!g_base) g_base = (uint8_t*)GetModuleHandleW(NULL);
    void* engine = *(void**)AT(RVA_GENGINE);
    FName gnd = NameFromEName(NAME_GameNetDriver);
    Log("iris cvar=%d", *(int32_t*)AT(RVA_CVAR_USE_IRIS));
    TArrayRaw* defs = (TArrayRaw*)((uint8_t*)engine + OFF_ENGINE_NETDRIVERDEFS);
    for (int i = 0; i < defs->Num; i++) {
        uint32_t* e = (uint32_t*)(defs->Data + (size_t)i * SIZEOF_NETDRIVERDEF);
        Log("iris config[%d]: def=%u/%u name=%u/%u wild=%u/%u flags=%08x %08x gnd=%u", i, e[0], e[1], e[2], e[3], e[4], e[5], e[8], e[9], gnd.ComparisonIndex);
    }
    TArrayRaw* list = (TArrayRaw*)((uint8_t*)engine + OFF_ENGINE_WORLDLIST);
    for (int i = 0; i < list->Num; i++) {
        uint8_t* ctx = ((uint8_t**)list->Data)[i];
        if (!ctx) continue;
        void* gi = *(void**)(ctx + 0x208);
        char ov = gi ? ((GIIrisOverride_t)VFunc(gi, 0x430))(gi, gnd) : -1;
        char cmd = ((CmdlineIris_t)AT(0x1B61C00))();
        char will = ((WillUseIris_t)AT(0x3BAC850))(engine, ctx, gnd, gnd);
        Log("iris ctx[%d]=%p world=%p gi=%p giOverride=%d cmdline=%d decision=%d", i, ctx, *(void**)(ctx + OFF_CONTEXT_WORLD), gi, ov, cmd, will);
    }
}

__declspec(dllexport) int lanppart_debug_iris(void* L) {
    (void)L;
    DebugIris();
    return 0;
}

// ---- Iris ownership of remote players' actors ----
typedef void* (*GetRepBridge_t)(void* rs);
typedef uint64_t* (*GetActorHandle_t)(void* bridge, uint64_t* outHandle, void* actor, int flags);
typedef uint32_t (*GetOwningConn_t)(void* rs, uint64_t handle);
typedef void (*UpdateOwningNetConnection_t)(void* actor);

static uint8_t* WorldGameNetDriver(void) {
    void* engine = *(void**)AT(RVA_GENGINE);
    TArrayRaw* list = (TArrayRaw*)((uint8_t*)engine + OFF_ENGINE_WORLDLIST);
    for (int i = 0; i < list->Num; i++) {
        uint8_t* ctx = ((uint8_t**)list->Data)[i];
        uint8_t* world = ctx ? *(uint8_t**)(ctx + OFF_CONTEXT_WORLD) : NULL;
        if (world && *(uint8_t**)(world + OFF_WORLD_NETDRIVER)) return *(uint8_t**)(world + OFF_WORLD_NETDRIVER);
    }
    return NULL;
}

static uint32_t OwnerOf(uint8_t* rs, void* actor) {
    void* bridge = ((GetRepBridge_t)AT(RVA_RS_GET_BRIDGE))(rs);
    uint64_t handle = 0;
    if (bridge) ((GetActorHandle_t)AT(RVA_BRIDGE_GET_ACTOR_HANDLE))(bridge, &handle, actor, 0);
    if (!handle) return 0xFFFFFFFF;
    return ((GetOwningConn_t)AT(RVA_RS_GET_OWNING_CONN))(rs, handle);
}

typedef char (*SetFilter_t)(void* rs, uint64_t handle, uint32_t filter, FName profile);
#define TO_OWNER_FILTER 1

// Player controllers must only replicate to their owner: a client that receives someone else's controller
// first binds its local player to it. The bridge doesn't apply the owner filter in this build, so do it.
static void FilterToOwner(uint8_t* rs, void* pc) {
    static uint64_t done[64];
    static int doneCount;
    static uint8_t* doneFor;  // handles are only unique within one replication system
    if (doneFor != rs) {
        doneFor = rs;
        doneCount = 0;
    }
    void* bridge = ((GetRepBridge_t)AT(RVA_RS_GET_BRIDGE))(rs);
    uint64_t handle = 0;
    if (bridge) ((GetActorHandle_t)AT(RVA_BRIDGE_GET_ACTOR_HANDLE))(bridge, &handle, pc, 0);
    if (!handle) return;
    for (int i = 0; i < doneCount; i++)
        if (done[i] == handle) return;
    FName none = { 0, 0 };
    // Fails while the object isn't fully registered yet: retried on the next refresh.
    if (!((SetFilter_t)AT(RVA_RS_SET_FILTER))(rs, handle, TO_OWNER_FILTER, none)) return;
    if (doneCount < 64) done[doneCount++] = handle;
    Log("owner filter on controller %p (handle %llx)", pc, (unsigned long long)handle);
}

static uint8_t* LocalPlayerController(void) {
    void* engine = *(void**)AT(RVA_GENGINE);
    TArrayRaw* list = (TArrayRaw*)((uint8_t*)engine + OFF_ENGINE_WORLDLIST);
    for (int i = 0; i < list->Num; i++) {
        uint8_t* ctx = ((uint8_t**)list->Data)[i];
        uint8_t* world = ctx ? *(uint8_t**)(ctx + OFF_CONTEXT_WORLD) : NULL;
        uint8_t* gi = world ? *(uint8_t**)(world + OFF_WORLD_GAMEINSTANCE) : NULL;
        TArrayRaw* players = gi ? (TArrayRaw*)(gi + OFF_GAMEINSTANCE_LOCALPLAYERS) : NULL;
        if (players && players->Num > 0) {
            uint8_t* lp = ((uint8_t**)players->Data)[0];
            if (lp) return *(uint8_t**)(lp + OFF_PLAYER_PLAYERCONTROLLER);
        }
    }
    return NULL;
}

// Server only: applies Iris connection ownership for every remote player controller (and, through the owner
// chain, its player state and pawn) and restricts every controller to its owner. Logs more when `verbose`.
static void RefreshOwnership(int verbose) {
    if (!g_base) g_base = (uint8_t*)GetModuleHandleW(NULL);
    uint8_t* driver = WorldGameNetDriver();
    if (!driver || *(void**)(driver + OFF_NETDRIVER_SERVERCONNECTION)) return;
    uint8_t* rs = *(uint8_t**)(driver + OFF_NETDRIVER_REPLICATIONSYSTEM);
    if (!rs) return;
    uint8_t* localPc = LocalPlayerController();
    if (localPc) FilterToOwner(rs, localPc);
    TArrayRaw* conns = (TArrayRaw*)(driver + OFF_NETDRIVER_CLIENTCONNECTIONS);
    for (int i = 0; i < conns->Num; i++) {
        uint8_t* conn = ((uint8_t**)conns->Data)[i];
        uint8_t* pc = conn ? *(uint8_t**)(conn + OFF_PLAYER_PLAYERCONTROLLER) : NULL;
        if (!pc) continue;
        uint32_t connId = *(uint32_t*)(conn + OFF_NETCONNECTION_PARENTCONNID);
        uint32_t before = OwnerOf(rs, pc);
        if (before != connId) ((UpdateOwningNetConnection_t)AT(RVA_ACTOR_UPDATE_OWNING_CONN))(pc);
        if (verbose || before != connId)
            Log("ownership: conn %d id=%u pc=%p owner %u -> %u", i, connId, pc, before, OwnerOf(rs, pc));
        FilterToOwner(rs, pc);
    }
}

// Every controller joining a hosted session (players and bots) gets its owner and the owner filter right
// away, before the next network update: refreshing them periodically left a window where clients
// received other controllers (a client then crashes on a bot's controller, which has no camera).
typedef void (*PostLogin_t)(void* gm, void* pc);
#define RVA_GM_POSTLOGIN           0x42DA750  // APGGameplayGameMode::PostLogin
#define RVA_GM_VTABLE_POSTLOGIN    0x5D17BC0  // its slot in the game mode's vtable

static void GameModePostLogin(uint8_t* gm, uint8_t* pc) {
    ((PostLogin_t)AT(RVA_GM_POSTLOGIN))(gm, pc);
    uint8_t* driver = WorldGameNetDriver();
    if (!pc || !driver || *(void**)(driver + OFF_NETDRIVER_SERVERCONNECTION)) return;
    uint8_t* rs = *(uint8_t**)(driver + OFF_NETDRIVER_REPLICATIONSYSTEM);
    if (!rs) return;
    ((UpdateOwningNetConnection_t)AT(RVA_ACTOR_UPDATE_OWNING_CONN))(pc);
    FilterToOwner(rs, pc);
}

static int PatchPostLogin(void) {
    void** slot = (void**)AT(RVA_GM_VTABLE_POSTLOGIN);
    if (*slot != AT(RVA_GM_POSTLOGIN)) {
        Log("PostLogin hook refused: unexpected vtable");
        return 0;
    }
    void* handler = (void*)&GameModePostLogin;
    if (!WriteCode(slot, &handler, sizeof(handler))) return 0;
    Log("PostLogin hooked");
    return 1;
}

// Dev diagnostics: Iris handle of the actor whose address was last written to lanppart_probe.txt.
__declspec(dllexport) int lanppart_debug_handles(void* L) {
    (void)L;
    if (!g_base) g_base = (uint8_t*)GetModuleHandleW(NULL);
    uint8_t* driver = WorldGameNetDriver();
    uint8_t* rs = driver ? *(uint8_t**)(driver + OFF_NETDRIVER_REPLICATIONSYSTEM) : NULL;
    if (!rs) { Log("handles: no replication system"); return 0; }
    void* bridge = ((GetRepBridge_t)AT(RVA_RS_GET_BRIDGE))(rs);
    char path[MAX_PATH];
    ModFile(path, "lanppart_probe.txt");
    FILE* f = fopen(path, "r");
    if (!f) return 0;
    unsigned long long addr;
    while (fscanf(f, "%llx", &addr) == 1) {
        uint64_t handle = 0;
        ((GetActorHandle_t)AT(RVA_BRIDGE_GET_ACTOR_HANDLE))(bridge, &handle, (void*)(uintptr_t)addr, 0);
        Log("handles: actor %llx -> handle %llx (id %llu) owner %u", addr, (unsigned long long)handle,
            (unsigned long long)(handle & 0xFFFFFFFFFFull) >> 1, OwnerOf(rs, (void*)(uintptr_t)addr));
    }
    fclose(f);
    return 0;
}

__declspec(dllexport) int lanppart_refresh_ownership(void* L) {
    (void)L;
    RefreshOwnership(0);
    return 0;
}

// Server, called periodically by the Lua host: game state upkeep the game doesn't do itself.
__declspec(dllexport) int lanppart_server_tick(void* L) {
    (void)L;
    if (!g_installed) return 0;
    void* engine = *(void**)AT(RVA_GENGINE);
    TArrayRaw* list = (TArrayRaw*)((uint8_t*)engine + OFF_ENGINE_WORLDLIST);
    for (int i = 0; i < list->Num; i++) {
        uint8_t* ctx = ((uint8_t**)list->Data)[i];
        uint8_t* world = ctx ? *(uint8_t**)(ctx + OFF_CONTEXT_WORLD) : NULL;
        uint8_t* driver = world ? *(uint8_t**)(world + OFF_WORLD_NETDRIVER) : NULL;
        // Only our hosted sessions have a net driver without a server connection.
        if (!driver || *(void**)(driver + OFF_NETDRIVER_SERVERCONNECTION)) continue;
        uint8_t* gs = *(uint8_t**)(world + OFF_WORLD_GAMESTATE);
        if (!gs) continue;
        ResolveKnockdownRoulettes(gs);
        ResumeWriters(gs);
    }
    return 0;
}

// ---- Text catalog for the launcher ----
// UPGTextsDatabaseSaveGame::texts is a TMap<FString language, FPGTextDataArray>; iterating TMaps from UE4SS
// Lua intermittently crashes, so the Lua side passes the object here, through lanppart_request_<id>.txt (next to
// this DLL): "<address hex>\n<output file>". Output, UTF-8: "lang\tid\tdifficulty\tmodes\ttitle" lines.
#define OFF_TEXTDB_TEXTS        0x28
#define SIZEOF_TEXTMAP_ELEMENT  0x28   // TSetElement<TPair<FString, FPGTextDataArray>>
#define SIZEOF_TEXTDATA         0x40   // FPGTextData: ID, TypeModes, DifficultyLevel, Title, Body

static void WriteUtf8(FILE* f, const wchar_t* s, int len) {
    char buf[1024];
    if (!s || len <= 0) return;
    int n = WideCharToMultiByte(CP_UTF8, 0, s, len, buf, sizeof(buf) - 1, NULL, NULL);
    for (int i = 0; i < n; i++) if (buf[i] == '\t' || buf[i] == '\n' || buf[i] == '\r') buf[i] = ' ';
    fwrite(buf, 1, (size_t)n, f);
}

static const wchar_t* FStringData(const uint8_t* fstring, int* len) {
    TArrayRaw* a = (TArrayRaw*)fstring;
    *len = a->Num > 0 ? a->Num - 1 : 0;  // without the terminating zero
    return (const wchar_t*)a->Data;
}

__declspec(dllexport) int lanppart_dump_texts(void* L) {
    (void)L;
    char request[1024] = { 0 };
    // One request file per game process (servers and players share the mod's folder).
    const char* id = getenv("LANPPART3_ID");
    char name[96], requestPath[MAX_PATH];
    snprintf(name, sizeof(name), "lanppart_request_%s.txt", id && *id ? id : "game");
    ModFile(requestPath, name);
    FILE* rf = fopen(requestPath, "rb");
    if (!rf) return 0;
    size_t got = fread(request, 1, sizeof(request) - 1, rf);
    fclose(rf);
    remove(requestPath);
    request[got] = 0;
    char* nl = strchr(request, '\n');
    if (!nl) return 0;
    *nl = 0;
    char* outPath = nl + 1;
    outPath[strcspn(outPath, "\r\n")] = 0;
    uint8_t* db = (uint8_t*)(uintptr_t)_strtoui64(request, NULL, 16);
    if (!db) return 0;

    uint8_t* map = db + OFF_TEXTDB_TEXTS;
    TArrayRaw* elements = (TArrayRaw*)map;
    const uint32_t* inlineBits = (const uint32_t*)(map + 0x10);
    const uint32_t* secondaryBits = *(const uint32_t**)(map + 0x20);
    const uint32_t* bits = secondaryBits ? secondaryBits : inlineBits;
    FILE* out = fopen(outPath, "wb");
    if (!out) return 0;
    int count = 0;
    for (int32_t i = 0; i < elements->Num; i++) {
        if (!((bits[i / 32] >> (i % 32)) & 1)) continue;  // free slot
        uint8_t* elem = elements->Data + (size_t)i * SIZEOF_TEXTMAP_ELEMENT;
        int langLen;
        const wchar_t* lang = FStringData(elem, &langLen);
        TArrayRaw* texts = (TArrayRaw*)(elem + 0x10);
        for (int32_t t = 0; t < texts->Num; t++) {
            uint8_t* td = texts->Data + (size_t)t * SIZEOF_TEXTDATA;
            WriteUtf8(out, lang, langLen);
            fprintf(out, "\t%d\t%d\t", *(int32_t*)td, *(int32_t*)(td + 0x18));
            TArrayRaw* modes = (TArrayRaw*)(td + 0x08);
            for (int32_t m = 0; m < modes->Num; m++) fprintf(out, m ? ",%d" : "%d", modes->Data[m]);
            fputc('\t', out);
            int titleLen;
            const wchar_t* title = FStringData(td + 0x20, &titleLen);
            WriteUtf8(out, title, titleLen);
            fputc('\n', out);
            count++;
        }
    }
    fclose(out);
    Log("Text catalog: %d texts written", count);
    return 0;
}

// Dedicated server instance (V2): sessions wait for lanppart_server_start.
__declspec(dllexport) int lanppart_server_mode_on(void* L) {
    (void)L;
    if (!g_installed) return 0;
    if (!g_serverMode && !PatchServer()) return 0;
    g_serverMode = 1;
    return 0;
}

// Organizer's start: adds the requested bots, then starts the session's intro.
__declspec(dllexport) int lanppart_server_start(void* L) {
    (void)L;
    uint8_t* gm = g_serverMode ? HostedGameMode() : NULL;
    if (!gm) { Log("Server start: no hosted session"); return 0; }
    int bots = SpawnRequestedBots(gm);
    Log("Server start: %d bots added, starting the session", bots);
    ((GmVoid_t)AT(RVA_SET_SESSION_INTRO))(gm);
    return 0;
}

// Lives for the next hosted session (lobby roulette unchecked): see LoseLife.
__declspec(dllexport) int lanppart_lives_on(void* L) {
    (void)L;
    g_livesMode = 1;
    Log("Lives mode on");
    return 0;
}

__declspec(dllexport) int lanppart_lives_off(void* L) {
    (void)L;
    g_livesMode = 0;
    return 0;
}

__declspec(dllexport) int lanppart_debug_ownership(void* L) {
    (void)L;
    RefreshOwnership(1);
    return 0;
}

__declspec(dllexport) int lanppart_install(void* L) {
    (void)L;
    Install();
    return 0;
}

// Official servers use generic replication: Iris must be off before connecting to them.
static void DisableIris(void) {
    if (!g_base) g_base = (uint8_t*)GetModuleHandleW(NULL);
    void* engine = *(void**)AT(RVA_GENGINE);
    *(int32_t*)AT(RVA_CVAR_USE_IRIS) = 0;
    FName gameNetDriver = NameFromEName(NAME_GameNetDriver);
    TArrayRaw* defs = (TArrayRaw*)((uint8_t*)engine + OFF_ENGINE_NETDRIVERDEFS);
    for (int i = 0; i < defs->Num; i++) {
        uint8_t* def = defs->Data + (size_t)i * SIZEOF_NETDRIVERDEF;
        if (memcmp(def, &gameNetDriver, sizeof(FName)) == 0) def[OFF_NETDRIVERDEF_USEIRIS] = 0;
    }
    Log("Iris disabled");
}

__declspec(dllexport) int lanppart_enable_iris(void* L) {
    (void)L;
    EnableIris();
    return 0;
}

__declspec(dllexport) int lanppart_disable_iris(void* L) {
    (void)L;
    DisableIris();
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved) {
    (void)inst; (void)reason; (void)reserved;
    return TRUE;
}
