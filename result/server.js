var express = require('express'),
    async = require('async'),
    sql = require('mssql'), //mssql replaces Pool which was used for pgsql.
    cookieParser = require('cookie-parser'),
    app = express(),
    server = require('http').Server(app),
    io = require('socket.io')(server);

var port = process.env.PORT || 4000;

io.on('connection', function (socket) {

  socket.emit('message', { text : 'Welcome!' });

  socket.on('subscribe', function (data) {
    socket.join(data.channel);
  });
});

//Create the config for the SQL Server connection
var config = {
  user:'sa',
  password:'YourStrong@Password',
  server:'localhost',
  database:'Master',
  port:1433,
}

async.retry(
  {times: 1000, interval: 1000},
  function(callback) {
    //Drop the done from our callback function arg
    //Also pass in the config
    sql.connect(config, (err, client) => {
      if (err) {
        console.error("Waiting for db");
      }
      callback(err, client);
    });
  },
  function(err, client) {
    if (err) {
      return console.error("Giving up");
    }
    console.log("Connected to db");
    getVotes(client);
  }
);

function getVotes(client) {
  sql.query('SELECT vote, COUNT(id) AS count FROM votes GROUP BY vote', [], (err, result) => {
    if (err) {
      console.error("Error performing query: " + err);
    } else {
      var votes = collectVotesFromResult(result);
      io.sockets.emit("scores", JSON.stringify(votes));
    }

    setTimeout(function() {getVotes(client) }, 1000);
  });
}

function collectVotesFromResult(result) {
  var votes = {a: 0, b: 0, c: 0};

  result.rows.forEach(function (row) {
    votes[row.vote] = parseInt(row.count);
  });

  return votes;
}

app.use(cookieParser());
app.use(express.urlencoded());
app.use(express.static(__dirname + '/views'));

app.get('/', function (req, res) {
  res.sendFile(path.resolve(__dirname + '/views/index.html'));
});

//Handle errors during connections
sql.on('error', err => {
  console.error('SQL Server error:',err);
});

server.listen(port, function () {
  var port = server.address().port;
  console.log('App running on port ' + port);
});
