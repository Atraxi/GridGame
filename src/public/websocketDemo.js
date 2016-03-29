//Establish the WebSocket connection and set up event handlers
var webSocket;

//Initialize the websocket
id("initSocket").addEventListener("click", function () {
    webSocket = new WebSocket("ws://" + location.hostname + "/gamesocket?test=asdf&test=qwer&otherTest=ghjk");
    webSocket.onmessage = function (msg) { parseMessage(msg); };
    webSocket.onclose = function () { console.log("WebSocket connection closed"); };
    webSocket.onopen = function () {sendMessage("connection open"); };
});

//Initialize relevant session data


//Send message if "Send" is clicked
id("send").addEventListener("click", function () {
    sendMessage(id("message").value);
});

//Send message if enter is pressed in the input field
id("message").addEventListener("keypress", function (e) {
    if (e.keyCode === 13) { sendMessage(e.target.value); }
});

//Send a message if it's not empty, then clear the input field
function sendMessage(message) {
    if (message !== "") {
        webSocket.send(message);
        id("message").value = "";
    }
}

//Update the chat-panel, and the list of connected users
function parseMessage(msg) {
    var data = JSON.parse(msg.data);
    insert("chat", data.received);
    //id("userlist").innerHTML = "";
//     data.userlist.forEach(function (user) {
//         insert("userlist", "<li>" + user + "</li>");
//     });
}

//Helper function for inserting HTML as the first child of an element
function insert(targetId, message) {
    id(targetId).insertAdjacentHTML("afterbegin", message);
}

//Helper function for selecting element by id
function id(id) {
    return document.getElementById(id);
}