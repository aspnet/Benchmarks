getFileData = function (inputFile) {
    // return readUploadedFileAsText(inputFile);

    // Create the XHR request
    var request = new XMLHttpRequest();

    // Return it as a Promise
    return new Promise(function (resolve, reject) {

        // Setup our listener to process compeleted requests
        request.onreadystatechange = function () {

            // Only run if the request is complete
            if (request.readyState !== 4) return;

            // Process the response
            if (request.status >= 200 && request.status < 300) {
                // If successful
                resolve(request.responseText + ";" + inputFile.files[0].name);
            } else {
                // If failed
                reject(new DOMException(request.statusText));
            }
        };

        var $data = new FormData();
        $data.append('content', inputFile.files[0]);

        request.open('POST', '/api/upload/file', true);
        request.send($data);

    });
};

function scrollToBottom(elem) {
    elem.scrollTop = elem.scrollHeight;
}

$(function () {
    
});