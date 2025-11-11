using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using MySql.Data.MySqlClient;
using System.Net;
using System.Data;
using Function.DTO;
using Function.Constant;
using static Function.Constant.Constant;
using BCrypt.Net;

// JSON 직렬화 설정
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Function
{
    public class Function
    {
        private readonly string dbHost;
        private readonly string dbUser;
        private readonly string dbPass;
        private readonly string dbAccountSchema; // account_schema
        private readonly string dbGameSchema;    // Game_schema

        public Function()
        {
            // 1. 환경 변수에서 DB 접속 정보 로드
            dbHost = Environment.GetEnvironmentVariable("DB_HOST");
            dbUser = Environment.GetEnvironmentVariable("DB_USER");
            dbPass = Environment.GetEnvironmentVariable("DB_PASSWORD");
            dbAccountSchema = Environment.GetEnvironmentVariable("DB_ACCOUNT_SCHEMA");
            dbGameSchema = Environment.GetEnvironmentVariable("DB_GAME_SCHEMA");
        }

        /// <summary>
        /// /auth/login POST
        /// </summary>
        public async Task<APIGatewayProxyResponse> Login(APIGatewayProxyRequest request, ILambdaContext context)
        {
            // Unity가 보낸 JSON Body를 파싱
            RequestLogin loginRequest = new RequestLogin();
            try
            {
                loginRequest = JsonSerializer.Deserialize<RequestLogin>(request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("JSON 파싱 오류: {0}"), e.Message);
                return CreateResponse(HttpStatusCode.BadRequest, "요청 형식이 잘못되었습니다.");
            }

            try
            {
                string connString = string.Format("Server={0};Database={1};User={2};Password={3};", dbHost, dbAccountSchema, dbUser, dbPass);
                await using var conn = new MySqlConnection(connString);
                await conn.OpenAsync();

                // 3. ID를 기반으로 'UserInfo' 테이블에서 모든 정보를 취득
                string sql = @"SELECT UID, PW, Name, Level, EXP, ClearedStageCode, HP, ATK FROM `UserInfo` WHERE ID = @ID";
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@ID", loginRequest.id);

                await using var reader = await cmd.ExecuteReaderAsync();

                // 4. 유저 존재 여부 확인
                if (await reader.ReadAsync())
                {
                    // 5. (유저 존재) PW 해시 값 비교
                    string storedHash = reader.GetString("PW");
                    if (!BCrypt.Net.BCrypt.Verify(loginRequest.pw, storedHash))
                    {
                        // (실패) 비밀번호 불일치
                        context.Logger.LogWarning(string.Format("로그인 실패 (PW 불일치): {0}", loginRequest.id));
                        return CreateResponse(HttpStatusCode.Unauthorized, "ID 또는 비밀번호가 일치하지 않습니다.");
                    }

                    // 6. (성공) 로그인 성공
                    UserInfo user = new UserInfo();
                    user.uid = reader.GetInt32("UID");
                    user.name = reader.GetString("Name");
                    user.level = reader.GetInt32("Level");
                    user.hp = reader.GetInt32("HP");
                    user.atk = reader.GetInt32("ATK");
                    user.exp = reader.GetInt32("EXP");
                    int clearedStageCodeOrdinal = reader.GetOrdinal("ClearedStageCode");
                    user.clearStageCode = reader.IsDBNull(clearedStageCodeOrdinal) ? null : reader.GetString(clearedStageCodeOrdinal);
                    return CreateResponse(HttpStatusCode.OK, user);
                }
                else
                {
                    // 7. (실패) 일치하는 유저 없음
                    context.Logger.LogWarning(string.Format("로그인 실패 (ID 없음): {0}", loginRequest.id));
                    return CreateResponse(HttpStatusCode.Unauthorized, "ID 또는 비밀번호가 일치하지 않습니다.");
                }
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("[DB 오류 : {0}]", e.Message));
                return CreateResponse(HttpStatusCode.InternalServerError, "서버 오류가 발생했습니다.");
            }
        }
        /// <summary>
        /// /auth/register POST
        /// </summary>
        public async Task<APIGatewayProxyResponse> Register(APIGatewayProxyRequest request, ILambdaContext context)
        {
            RequestRegister registerRequest;
            try
            {
                registerRequest = JsonSerializer.Deserialize<RequestRegister>(request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("JSON 파싱 오류: {0}"), e.Message);
                return CreateResponse(HttpStatusCode.BadRequest, "요청 형식이 잘못되었습니다.");
            }

            try
            {
                // 1. account_schema에 접속
                string connString = string.Format("Server={0};Database={1};User={2};Password={3};", dbHost, dbAccountSchema, dbUser, dbPass);
                await using var conn = new MySqlConnection(connString);
                await conn.OpenAsync();

                // 2. 비밀번호 해시
                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(registerRequest.pw);

                // 3. DB에 삽입 (UserInfo 테이블의 기본값(Default)을 활용)
                string sql = "INSERT INTO `UserInfo` (ID, PW, Name) VALUES (@ID, @PW, @Name)";
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@ID", registerRequest.id);
                cmd.Parameters.AddWithValue("@PW", hashedPassword);
                cmd.Parameters.AddWithValue("@Name", registerRequest.name);

                await cmd.ExecuteNonQueryAsync();

                return CreateResponse(HttpStatusCode.Created, "회원가입 성공");
            }
            catch (MySqlException e)
            {
                if (e.Number == 1062) // 1062 = Duplicate entry (ID 중복)
                {
                    context.Logger.LogWarning(string.Format("회원가입 실패 (ID 중복): {0}", registerRequest.id));
                    return CreateResponse(HttpStatusCode.Conflict, "이미 존재하는 ID입니다.");
                }
                context.Logger.LogError(string.Format("[DB 오류 : {0}]", e.Message));
                return CreateResponse(HttpStatusCode.InternalServerError, "서버 오류 (DB)");
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("[일반 오류 : {0}]", e.Message));
                return CreateResponse(HttpStatusCode.InternalServerError, "서버 오류가 발생했습니다.");
            }
        }

        /// <summary>
        /// /stage/{stageId} GET
        /// </summary>
        public async Task<APIGatewayProxyResponse> GetStageInfo(APIGatewayProxyRequest request, ILambdaContext context)
        {
            if (!request.PathParameters.TryGetValue("stageId", out var stageId))
            {
                return CreateResponse(HttpStatusCode.BadRequest, "stageId가 필요합니다.");
            }

            try
            {
                // Game_schema에 접속
                string connString = string.Format("Server={0};Database={1};User={2};Password={3};", dbHost, dbGameSchema, dbUser, dbPass);
                await using var conn = new MySqlConnection(connString);
                await conn.OpenAsync();

                string sql = "SELECT StageCode, StageName, MonsterCount, MonsterCode1, MonsterCode2, MonsterCode3, PrerequisiteStage FROM `Stage` WHERE StageCode = @StageCode";
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@StageCode", stageId);

                await using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    Stage stageInfo = new Stage
                    {
                        stageCode = reader.GetString("StageCode"),
                        stageName = reader.GetString("StageName"),
                        monsterCount = reader.GetInt32("MonsterCount"),
                        monsterCode1 = reader.IsDBNull(reader.GetOrdinal("MonsterCode1")) ? null : reader.GetString("MonsterCode1"),
                        monsterCode2 = reader.IsDBNull(reader.GetOrdinal("MonsterCode2")) ? null : reader.GetString("MonsterCode2"),
                        monsterCode3 = reader.IsDBNull(reader.GetOrdinal("MonsterCode3")) ? null : reader.GetString("MonsterCode3"),
                        prerequisiteStage = reader.IsDBNull(reader.GetOrdinal("PrerequisiteStage")) ? null : reader.GetString("PrerequisiteStage")
                    };
                    return CreateResponse(HttpStatusCode.OK, stageInfo);
                }
                else
                {
                    return CreateResponse(HttpStatusCode.NotFound, "스테이지 정보를 찾을 수 없습니다.");
                }
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("[DB 오류 : {0}]", e.Message));
                return CreateResponse(HttpStatusCode.InternalServerError, "서버 오류가 발생했습니다.");
            }
        }

        /// <summary>
        /// /stage/{monsterCode} GET
        /// </summary>
        public async Task<APIGatewayProxyResponse> GetMonsterInfo(APIGatewayProxyRequest request, ILambdaContext context)
        {
            if (!request.PathParameters.TryGetValue("monsterCode", out var monsterCode))
            {
                return CreateResponse(HttpStatusCode.BadRequest, "monsterCode가 필요합니다.");
            }

            try
            {
                // Game_schema에 접속
                string connString = string.Format("Server={0};Database={1};User={2};Password={3};", dbHost, dbGameSchema, dbUser, dbPass);
                await using var conn = new MySqlConnection(connString);
                await conn.OpenAsync();

                string sql = "SELECT MonsterCode, MonsterName, Grade, HP, ATK, RewardEXP FROM `Monster` WHERE MonsterCode = @MonsterCode";
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@MonsterCode", monsterCode);

                await using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    string grade = reader.GetString("Grade");
                    Enum.TryParse(grade, true, out MonsterGrade parsedGrade);
                    Monster monsterInfo = new Monster
                    {
                        monsterCode = reader.GetString("MonsterCode"),
                        monsterName = reader.GetString("MonsterName"),
                        grade = parsedGrade,
                        hp = reader.GetInt32("HP"),
                        atk = reader.GetInt32("ATK"),
                        rewardExp = reader.GetInt32("RewardEXP")
                    };
                    return CreateResponse(HttpStatusCode.OK, monsterInfo);
                }
                else
                {
                    return CreateResponse(HttpStatusCode.NotFound, "몬스터 정보를 찾을 수 없습니다.");
                }
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("[DB 오류 : {0}]", e.Message));
                return CreateResponse(HttpStatusCode.InternalServerError, "서버 오류가 발생했습니다.");
            }
        }

        /// <summary>
        /// /stage/{CardCode} GET
        /// </summary>
        public async Task<APIGatewayProxyResponse> GetCardInfo(APIGatewayProxyRequest request, ILambdaContext context)
        {
            if (!request.PathParameters.TryGetValue("CardCode", out var cardCode))
            {
                return CreateResponse(HttpStatusCode.BadRequest, "CardCode 필요합니다.");
            }

            try
            {
                // Game_schema에 접속
                string connString = string.Format("Server={0};Database={1};User={2};Password={3};", dbHost, dbGameSchema, dbUser, dbPass);
                await using var conn = new MySqlConnection(connString);
                await conn.OpenAsync();

                string sql = "SELECT CardCode, CardName, CardType, Cost, EffectValue, Description FROM `Card` WHERE CardCode = @CardCode";
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@CardCode", cardCode);

                await using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    string cardType = reader.GetString("CardType");
                    Enum.TryParse(cardType, true, out CardType parsedCardType);
                    Card cardInfo = new Card
                    {
                        cardCode = reader.GetString("CardCode"),
                        cardName = reader.GetString("CardName"),
                        cardType = parsedCardType,
                        cost = reader.GetInt32("Cost"),
                        effectValue = reader.GetInt32("EffectValue"),
                        description = reader.GetString("Description")
                    };
                    return CreateResponse(HttpStatusCode.OK, cardInfo);
                }
                else
                {
                    return CreateResponse(HttpStatusCode.NotFound, "카드 정보를 찾을 수 없습니다.");
                }
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("[DB 오류 : {0}]", e.Message));
                return CreateResponse(HttpStatusCode.InternalServerError, "서버 오류가 발생했습니다.");
            }
        }

        /// <summary>
        /// /stage/{Level} GET
        /// </summary>
        public async Task<APIGatewayProxyResponse> GetLevelInfo(APIGatewayProxyRequest request, ILambdaContext context)
        {
            if (!request.PathParameters.TryGetValue("LevelValue", out var levelValue))
            {
                return CreateResponse(HttpStatusCode.BadRequest, "Level 필요합니다.");
            }

            try
            {
                // Game_schema에 접속
                string connString = string.Format("Server={0};Database={1};User={2};Password={3};", dbHost, dbGameSchema, dbUser, dbPass);
                await using var conn = new MySqlConnection(connString);
                await conn.OpenAsync();

                string sql = "SELECT LevelValue, RequiredEXP, HP, ATK FROM `Level` WHERE LevelValue = @LevelValue";
                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@LevelValue", levelValue);

                await using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    Level levelInfo = new Level
                    {
                        levelValue = reader.GetInt32("LevelValue"),
                        requiredExp = reader.GetInt32("RequiredEXP"),
                        hp = reader.GetInt32("HP"),
                        atk = reader.GetInt32("ATK"),
                    };
                    return CreateResponse(HttpStatusCode.OK, levelInfo);
                }
                else
                {
                    return CreateResponse(HttpStatusCode.NotFound, "레벨 정보를 찾을 수 없습니다.");
                }
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("[DB 오류 : {0}]", e.Message));
                return CreateResponse(HttpStatusCode.InternalServerError, "서버 오류가 발생했습니다.");
            }
        }

        /// <summary>
        /// /user/progress 
        /// 유저의 게임 진행 상태 (레벨, 경험치, 스탯, 스테이지)를 저장(SET)
        /// </summary>
        public async Task<APIGatewayProxyResponse> SetUserInfo(APIGatewayProxyRequest request, ILambdaContext context)
        {
            // 1. UserInfo JSON Body를 파싱
            UserInfo userToUpdate;
            try
            {
                userToUpdate = JsonSerializer.Deserialize<UserInfo>(request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("JSON 파싱 오류: {0}"), e.Message);
                return CreateResponse(HttpStatusCode.BadRequest, "요청 형식이 잘못되었습니다.");
            }

            if (userToUpdate.uid <= 0)
                return CreateResponse(HttpStatusCode.BadRequest, "유저 UID가 필요합니다.");

            try
            {
                // 2. account_schema에 접속
                string connString = string.Format("Server={0};Database={1};User={2};Password={3};", dbHost, dbAccountSchema, dbUser, dbPass);
                await using var conn = new MySqlConnection(connString);
                await conn.OpenAsync();

                // 3. SQL UPDATE 문 실행
                string sql = @"UPDATE `UserInfo` SET Level = @Level, HP = @HP, ATK = @ATK, EXP = @EXP, ClearedStageCode = @ClearedStageCode WHERE UID = @UID";

                await using var cmd = new MySqlCommand(sql, conn);

                // 4. 파라미터 설정
                cmd.Parameters.AddWithValue("@Level", userToUpdate.level);
                cmd.Parameters.AddWithValue("@HP", userToUpdate.hp);
                cmd.Parameters.AddWithValue("@ATK", userToUpdate.atk);
                cmd.Parameters.AddWithValue("@EXP", userToUpdate.exp);
                cmd.Parameters.AddWithValue("@ClearedStageCode", (object)userToUpdate.clearStageCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UID", userToUpdate.uid);

                // 5. 쿼리 실행
                int rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    return CreateResponse(HttpStatusCode.OK, "유저 정보가 성공적으로 업데이트되었습니다.");
                }
                else
                {
                    context.Logger.LogWarning(string.Format("유저 정보 업데이트 실패 (UID 없음): {0}", userToUpdate.uid));
                    return CreateResponse(HttpStatusCode.NotFound, "업데이트할 유저를 찾을 수 없습니다.");
                }
            }
            catch (Exception e)
            {
                context.Logger.LogError(string.Format("[DB 오류 : {0}]", e.Message));
                return CreateResponse(HttpStatusCode.InternalServerError, "서버 오류가 발생했습니다.");
            }
        }

        // API Gateway 응답 생성 헬퍼
        private APIGatewayProxyResponse CreateResponse(HttpStatusCode statusCode, object result)
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)statusCode,
                Body = JsonSerializer.Serialize(result),
                Headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Access-Control-Allow-Origin", "*" }
            }
            };
        }
    }
}