"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/LlmChatHub").build();
// Get modelName from query parameter
function getQueryParam(name) {
    const params = new URLSearchParams(window.location.search);
    return params.get(name);
}
var modelName = "orchestrator"

document.getElementById("modelNameSpan").textContent = modelName;

//Disable the send button until connection is established.
document.getElementById("sendButton").disabled = true;

connection.start().then(function () {
    document.getElementById("sendButton").disabled = false;

    let botMessageDiv = null;

    // Trigger sendButton click on Ctrl+Enter in messageInput
    document.getElementById("messageInput").addEventListener("keydown", function (event) {
        if (event.ctrlKey && event.key === "Enter") {
            document.getElementById("sendButton").click();
            event.preventDefault();
        }
    });

    document.getElementById("sendButton").addEventListener("click", function (event) {
        var messageInput = document.getElementById("messageInput");
        var message = messageInput.value;

        if (message.trim() !== "") {
            var messagesList = document.getElementById("messagesList");
            var messageDiv = document.createElement("div");
            messageDiv.className = "userMessage";
            messageDiv.textContent = message;

            var wrapperDiv = document.createElement("div");
            wrapperDiv.style.textAlign = "right";
            wrapperDiv.appendChild(messageDiv);

            messagesList.appendChild(wrapperDiv);
            messagesList.scrollTop = messagesList.scrollHeight;

            botMessageDiv = document.createElement("div");
            botMessageDiv.className = "botMessage";
            const spinner = document.createElement("div");
            spinner.className = "spinner";
            botMessageDiv.appendChild(spinner);
            messagesList.appendChild(botMessageDiv);
            messagesList.scrollTop = messagesList.scrollHeight;

            connection.invoke("SendMessageToOrchestratorKernel", message).catch(function (err) {
                return console.error(err.toString());
            });
            messageInput.value = "";
        }

        event.preventDefault();
    });

    let fullMessage = "";
    const converter = new showdown.Converter();

    function flashElement(element) {
        element.classList.add("flash-animation");
        setTimeout(() => {
            element.classList.remove("flash-animation");
        }, 500); // Match the animation duration
    }

    connection.stream("LlmStream", modelName)
        .subscribe({
            next: (item) => {
                if (typeof item === 'string' && item.startsWith("Usage:")) {
                    const parts = item.split(':')[1].split(',');
                    if (parts.length === 3) {
                        const inputTokens = parts[0];
                        const outputTokens = parts[1];
                        const cost = parts[2];

                        const inputTokensSpan = document.getElementById("inputTokensSpan");
                        const outputTokensSpan = document.getElementById("outputTokensSpan");
                        const costSpan = document.getElementById("costSpan");

                        inputTokensSpan.textContent = `IN: ${inputTokens}`;
                        outputTokensSpan.textContent = `OUT: ${outputTokens}`;
                        costSpan.textContent = `${parseFloat(cost).toFixed(5)}💲`;

                        flashElement(inputTokensSpan);
                        flashElement(outputTokensSpan);
                        flashElement(costSpan);

                        return; // Don't display this message in the chat
                    }
                }
                else if (typeof item === 'string' && item.startsWith("[SendToModel]:")) {
                    const modelText = item.substring("[SendToModel]:".length).trim();
                    
                    // Get the iframe and insert text into its messageInput
                    const iframe = document.getElementById("currentModelIframe");
                    if (iframe && iframe.contentWindow) {
                        const iframeDoc = iframe.contentWindow.document;
                        const messageInput = iframeDoc.getElementById("messageInput");
                        if (messageInput) {
                            messageInput.value = modelText;
                        }

                         var sendButton = iframeDoc.getElementById('sendButton');
                        if (sendButton) {
                            sendButton.click();
                        }
                    }

                    return; // Don't display this message in the chat
                }
                else if (typeof item === 'string' && item.startsWith("[SwitchModel]:")) {
                    const modelText = item.substring("[SwitchModel]:".length).trim();
                    
                    // Replace the iframe with a new one using the new model
                    const iframe = document.getElementById("currentModelIframe");
                    if (iframe) {
                        const wasHidden = iframe.hasAttribute("hidden") || iframe.style.display === "none";
                        
                        if (wasHidden) {
                            // If it was hidden, just change src and fade in
                            iframe.src = `/Index?model=${modelText}`;
                            iframe.removeAttribute("hidden");
                            iframe.style.display = "block";
                            iframe.style.opacity = "0";
                            iframe.style.transition = "opacity 0.3s ease-in-out";
                            
                            // Fade in after a brief delay to ensure the new content starts loading
                            setTimeout(() => {
                                iframe.style.opacity = "1";
                            }, 100);
                        } else {
                            // If it was visible, fade out, change src, then fade in
                            iframe.style.transition = "opacity 0.3s ease-in-out";
                            iframe.style.opacity = "0";
                            
                            setTimeout(() => {
                                iframe.src = `/Index?model=${modelText}`;
                                iframe.removeAttribute("hidden");
                                iframe.style.display = "block";
                                
                                // Fade in after changing src
                                setTimeout(() => {
                                    iframe.style.opacity = "1";
                                }, 100);
                            }, 300); // Wait for fade out to complete
                        }
                    }

                    return; // Don't display this message in the chat
                }

                const spinner = botMessageDiv ? botMessageDiv.querySelector('.spinner') : null;

                if (item.trim() === "" && spinner) {
                    return; // Still waiting for content, do nothing.
                }

            
                if (spinner) {
                    botMessageDiv.innerHTML = ""; // Clear the spinner
                    fullMessage = "";
                }

                if (botMessageDiv === null) {
                    botMessageDiv = document.createElement("div");
                    botMessageDiv.className = "botMessage";
                    document.getElementById("messagesList").appendChild(botMessageDiv);
                    fullMessage = "";
                }

                fullMessage += item;
                const html = converter.makeHtml(fullMessage);
                botMessageDiv.innerHTML = html;
                const messagesList = document.getElementById("messagesList");
                messagesList.scrollTop = messagesList.scrollHeight;
            },
            complete: () => {
                fullMessage = "";
                botMessageDiv = null;
            },
            error: (err) => {
                if (botMessageDiv) {
                    botMessageDiv.innerHTML = `<p>Error: ${err}</p>`;
                } else {
                    document.getElementById("messagesList").innerHTML += `<p>Error: ${err}</p>`;
                }
                botMessageDiv = null;
            },
        })
}).catch(function (err) {
    return console.error(err.toString());
});

