// Standalone import: retained sponsor download validation; lottery rewards and controller callbacks removed.
using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

namespace MusicMidiPlayer.CommunityBoards
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MembershipAccess : UdonSharpBehaviour
    {
        public SponsorWallDisplay sponsorWall;
        public VRCUrl authorizationUrl;
        public string campaignId="16877351";
        [HideInInspector] public string lastResult="not_loaded";

        private const int MaxNames=1000, NamesPerFrame=32, MaxResponseCharacters=98304;
        private const float StartupRetrySeconds=60f, MaximumRequestAge=120f;
        private const int MaximumStartupAttempts=3;
        private bool running,authorized,inFlight,processing,candidateFound;
        private bool sessionResolved;
        private int sessionPlayerId=-1,startupAttempts;
        private int generation,requestGeneration,parseGeneration,parseIndex;
        private float nextServiceAt,nextRequestAt,requestStartedAt;
        private double requestStartedUtc,candidateGenerated,candidateExpires;
        private string requestUrl,requestName,parseName,parseUrl;
        private DataList pendingNames;
        private DataList pendingLevels;
        private int candidateLevel,sessionLevel;
        private VRCPlayerApi localPlayer;

        private double UtcSeconds(){return DateTime.UtcNow.Ticks/10000000d-62135596800d;}

        private void OnEnable()
        {
            running=true;generation++;processing=false;pendingNames=null;pendingLevels=null;
            localPlayer=VRC.SDKBase.Networking.LocalPlayer;
            nextServiceAt=0f;
            EnsureSession();

            // Never clear an outstanding download here: its old-generation callback must
            // drain before starting another request. This object is never pooled/reused.
        }
        // Disable/re-enable does not create a new world session or discard a completed result.
        private void OnDisable(){running=false;generation++;processing=false;pendingNames=null;pendingLevels=null;}
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if(!Utilities.IsValid(player)||!player.isLocal)return;
            localPlayer=player;EnsureSession();_RefreshMembership();
        }

        private void EnsureSession()
        {
            if(!Utilities.IsValid(localPlayer) || localPlayer.playerId==sessionPlayerId)return;
            sessionPlayerId=localPlayer.playerId;generation++;
            sessionResolved=false;startupAttempts=0;processing=false;pendingNames=null;pendingLevels=null;
            nextRequestAt=Time.realtimeSinceStartup;Revoke();
            if(sponsorWall!=null)sponsorWall._Clear();
        }

        private void Update()
        {
            if(!running)return;
            if(processing){ProcessNames();return;}
            // Once resolved (including not-a-member), there is no service pulse, expiry
            // polling or download for the rest of this world session.
            if(sessionResolved)return;
            float time=Time.realtimeSinceStartup;
            if(time<nextServiceAt)return;
            nextServiceAt=time+1f;
            if(!Utilities.IsValid(localPlayer))localPlayer=VRC.SDKBase.Networking.LocalPlayer;
            EnsureSession();
            if(inFlight && time-requestStartedAt>MaximumRequestAge)lastResult="waiting_for_download_callback";
            if(time>=nextRequestAt && !inFlight && !processing)BeginRequest();
        }

        // Freshness is checked when receiving/validating the entry response. Thereafter
        // consumption only reads the session decision, tied to the local player's ID.
        // Display-name changes and the response's 5-minute cache expiry do not revoke it.
        public bool HasEntitlement()
        {
            return running && sessionResolved && authorized && Utilities.IsValid(localPlayer)
                && localPlayer.playerId==sessionPlayerId;
        }

        // Cached session result, also used on voucher clicks without any network request.
        // 0 = no membership, 1 = standard, 2 = highest tier (or verified creator).
        public int MembershipLevel(){return HasEntitlement()?sessionLevel:0;}



        // Underscore entry point cannot be invoked as a legacy network custom event.
        // Kept for existing callers; after resolution it cannot refresh this session.
        public void _RefreshMembership()
        {
            if(!running || sessionResolved)return;
            EnsureSession();
            if(!inFlight && !processing && Time.realtimeSinceStartup>=nextRequestAt)BeginRequest();
        }

        private void BeginRequest()
        {
            if(sessionResolved || !Utilities.IsValid(localPlayer))return;
            if(VRCUrl.IsNullOrEmpty(authorizationUrl))
            {lastResult="missing_authorization_url";sessionResolved=true;Revoke();return;}
            if(startupAttempts>=MaximumStartupAttempts){sessionResolved=true;Revoke();return;}
            startupAttempts++;nextRequestAt=Time.realtimeSinceStartup+StartupRetrySeconds;
            requestUrl=authorizationUrl.Get();requestName=localPlayer.displayName;
            requestGeneration=generation;requestStartedAt=Time.realtimeSinceStartup;requestStartedUtc=UtcSeconds();
            inFlight=true;lastResult="loading";
            VRCStringDownloader.LoadUrl(authorizationUrl,(IUdonEventReceiver)this);
        }

        private bool AcceptCallback(IVRCStringDownload result)
        {
            if(!inFlight||result==null||VRCUrl.IsNullOrEmpty(result.Url)||result.Url.Get()!=requestUrl)return false;
            inFlight=false;
            if(!running||requestGeneration!=generation||Time.realtimeSinceStartup-requestStartedAt>MaximumRequestAge
                ||!Utilities.IsValid(localPlayer)||localPlayer.displayName!=requestName
                ||VRCUrl.IsNullOrEmpty(authorizationUrl)||authorizationUrl.Get()!=requestUrl)
            {lastResult="stale_callback";ScheduleStartupRetry();return false;}
            return true;
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if(!AcceptCallback(result))return;
            string raw=result.Result;
            DataToken root;
            if(string.IsNullOrEmpty(raw)||raw.Length>MaxResponseCharacters
                ||!VRCJson.TryDeserializeFromJson(raw,out root)||root.TokenType!=TokenType.DataDictionary)
            {Reject("invalid_json");return;}
            var data=root.DataDictionary;DataToken schema,status,campaign,entitlement,generated,expires,names;
            if(!data.TryGetValue("schemaVersion",TokenType.Double,out schema)||schema.Double!=1d
                ||!data.TryGetValue("status",TokenType.String,out status)||status.String!="enabled"
                ||!data.TryGetValue("campaignId",TokenType.String,out campaign)||campaign.String!=campaignId
                ||!data.TryGetValue("entitlement",TokenType.String,out entitlement)||entitlement.String!="direct_loss_burn"
                ||!data.TryGetValue("generatedAt",TokenType.Double,out generated)
                ||!data.TryGetValue("expiresAt",TokenType.Double,out expires)
                ||!data.TryGetValue("displayNames",TokenType.DataList,out names))
            {Reject("invalid_schema");return;}
            double utc=UtcSeconds();candidateGenerated=generated.Double;candidateExpires=expires.Double;
            if(!(candidateGenerated>0d
                && candidateGenerated>=requestStartedUtc-30d && candidateGenerated<=utc+30d
                && candidateExpires>utc && candidateExpires>candidateGenerated
                && candidateExpires<=candidateGenerated+300d))
            {Reject("expired_or_stale_response");return;}
            pendingNames=names.DataList;
            if(pendingNames.Count>MaxNames){Reject("response_capacity");return;}
            pendingLevels=null;
            DataToken levels;
            if(data.TryGetValue("membershipLevels",out levels))
            {
                if(levels.TokenType!=TokenType.DataList || levels.DataList.Count!=pendingNames.Count)
                {Reject("invalid_membership_levels");return;}
                pendingLevels=levels.DataList;
            }
            // Display work runs concurrently with permission validation and never gates
            // ticket consumption. Older servers without this optional field stay usable.
            if(sponsorWall!=null)
            {
                DataToken supporters;
                if(data.TryGetValue("supporterIndices",TokenType.DataList,out supporters))
                    sponsorWall._SetSnapshot(pendingNames,supporters.DataList,candidateExpires,requestUrl);
                else sponsorWall._Clear();
            }
            parseName=requestName;parseUrl=requestUrl;parseGeneration=generation;
            parseIndex=0;candidateFound=false;candidateLevel=0;processing=true;lastResult="processing";
            // Full JSON parsing is bounded by 96 Ki characters; name validation/search is
            // limited to 32 entries per frame. At 30fps, 1000 names take ~1.1s (estimate).
        }

        private void ProcessNames()
        {
            if(parseGeneration!=generation||!Utilities.IsValid(localPlayer)||localPlayer.displayName!=parseName
                ||VRCUrl.IsNullOrEmpty(authorizationUrl)||authorizationUrl.Get()!=parseUrl||candidateExpires<=UtcSeconds())
            {Reject("stale_processing");return;}
            int end=Mathf.Min(parseIndex+NamesPerFrame,pendingNames.Count);
            for(;parseIndex<end;parseIndex++)
            {
                DataToken entry=pendingNames[parseIndex];
                if(entry.TokenType!=TokenType.String||string.IsNullOrEmpty(entry.String)||entry.String.Length>80)
                {Reject("invalid_name");return;}
                int level=1; // Legacy responses can grant standard membership only.
                if(pendingLevels!=null)
                {
                    DataToken value=pendingLevels[parseIndex];
                    if(value.TokenType!=TokenType.Double || (value.Double!=1d && value.Double!=2d))
                    {Reject("invalid_membership_level");return;}
                    level=(int)value.Double;
                }
                if(entry.String==parseName){candidateFound=true;candidateLevel=Mathf.Max(candidateLevel,level);}
            }
            if(parseIndex<pendingNames.Count)return;
            processing=false;pendingNames=null;pendingLevels=null;
            double remaining=candidateExpires-UtcSeconds();
            if(remaining<=0d){Reject("expired_during_processing");return;}
            authorized=candidateFound;sessionResolved=true;
            sessionLevel=authorized?candidateLevel:0;
            lastResult=authorized?"authorized":"not_a_member";

        }



        public override void OnStringLoadError(IVRCStringDownload result)
        {
            if(!AcceptCallback(result))return;
            lastResult="download_error";
            Revoke();ScheduleStartupRetry();
        }
        private void Reject(string reason)
        {
            processing=false;pendingNames=null;pendingLevels=null;lastResult=reason;Revoke();
            if(sponsorWall!=null)sponsorWall._Clear();
            ScheduleStartupRetry();
        }

        private void ScheduleStartupRetry()
        {
            // Only failed entry authentication retries: max 3 downloads, 60s apart.
            // Never overlap a still-outstanding callback or retry a completed decision.
            if(sessionResolved)return;
            if(startupAttempts>=MaximumStartupAttempts){sessionResolved=true;return;}
            nextRequestAt=Time.realtimeSinceStartup+StartupRetrySeconds;
        }
        private void Revoke()
        {
            authorized=false;sessionLevel=0;


        }
    }
}
