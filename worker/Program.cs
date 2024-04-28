using System;
using System.Data.Common;
using System.Data.SqlClient; //This is used 
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Worker
{
    [ApiController]
    [Route("api")]
    public class Program : ControllerBase

    {
        public static int Main(string[] args)
        {
            try
            {
                var sqlConn = OpenDbConnection("Data Source=db,1433;Initial Catalog=master;User ID=sa;Password=YourStrong@Password;Encrypt=false");
                var redisConn = OpenRedisConnection("redis");
                var redis = redisConn.GetDatabase();

                var keepAliveCommand = sqlConn.CreateCommand();
                keepAliveCommand.CommandText = "SELECT 1";

                var definition = new { vote = "", voter_id = "" };
                while (true)
                {
                    // Slow down to prevent CPU spike, only query each 100ms
                    Thread.Sleep(100);

                    // Reconnect redis if down
                    if (redisConn == null || !redisConn.IsConnected) {
                        Console.WriteLine("Reconnecting Redis");
                        redisConn = OpenRedisConnection("redis");
                        redis = redisConn.GetDatabase();
                    }
                    string json = redis.ListLeftPopAsync("votes").Result;
                    if (json != null)
                    {
                        var vote = JsonConvert.DeserializeAnonymousType(json, definition);
                        Console.WriteLine($"Processing vote for '{vote.vote}' by '{vote.voter_id}'");
                        // Reconnect DB if down
                        if (!sqlConn.State.Equals(System.Data.ConnectionState.Open))
                        {
                            Console.WriteLine("Reconnecting DB");
                            sqlConn = OpenDbConnection("Data Source=db,1433;Initial Catalog=master;User Id=sa;Password=YourStrong@Password;Encrypt=false");
                        }
                        else
                        { // Normal +1 vote requested
                            UpdateVote(sqlConn, vote.voter_id, vote.vote);

                        }
                    }
                    else
                    {
                        keepAliveCommand.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static SqlConnection OpenDbConnection(string connectionString)
        {
            SqlConnection connection;

            while (true)
            {
                try
                {
                    connection = new SqlConnection(connectionString);
                    connection.Open();
                    break;
                }
                catch (SocketException)
                {
                    Console.Error.WriteLine("Waiting for db --Socket Ex");
                    Thread.Sleep(1000);
                }
                catch (DbException ex)
                {
                    Console.Error.WriteLine("Waiting for DB --DB Ex" + ex);
                    Thread.Sleep(1000);
                }
            }

            Console.Error.WriteLine("Connected to db");

            string createTableQuery = @"
                        CREATE TABLE votes (
                            id NVARCHAR(255) NOT NULL PRIMARY KEY,
                            vote NVARCHAR(255) NOT NULL
                        )";

            //var command = new SqlCommand("createVotesTable", connection);
            var command = new SqlCommand(createTableQuery, connection);
            //command.CommandType = CommandType.StoredProcedure;

            try
            {
                command.ExecuteNonQuery();
            } catch
            {
                //Do nothing
            }
            return connection;
            
        }

        private static ConnectionMultiplexer OpenRedisConnection(string hostname)
        {

            // Use IP address to workaround https://github.com/StackExchange/StackExchange.Redis/issues/410
            var ipAddress = GetIp(hostname);
            Console.WriteLine($"Found redis at {ipAddress}");

            while (true)
            {
                try
                {
                    Console.Error.WriteLine("Connecting to redis");
                    return ConnectionMultiplexer.Connect(ipAddress);
                }
                catch (RedisConnectionException)
                {
                    Console.Error.WriteLine("Waiting for redis");
                    Thread.Sleep(1000);
                }
            }
        }

        private static string GetIp(string hostname)
            => Dns.GetHostEntryAsync(hostname)
                .Result
                .AddressList
                .First(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToString();

        public static void UpdateVote(SqlConnection sqlConn, string voterId, string vote)
        {
            var command = sqlConn.CreateCommand();
            try
            {
                Console.Error.WriteLine("Adding a vote...");
                command.CommandText = "INSERT INTO votes (id, vote) VALUES (@id, @vote)";
                command.Parameters.AddWithValue("@id", voterId);
                command.Parameters.AddWithValue("@vote", vote);
                command.ExecuteNonQuery();
            }
            catch (DbException)
            {
                Console.Error.WriteLine("Updating a vote instead...");
                command.CommandText = "UPDATE votes SET vote = @vote WHERE id = @id";
                command.ExecuteNonQuery();
                Console.Error.WriteLine("Vote has been updated!");
            }
            finally
            {
                command.Dispose();
            }
       
        } 
    }

    public class Voter
    {
        public string voterId;
        public string vote;
    }
}