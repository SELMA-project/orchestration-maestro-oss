
function main() {
  _request.SetHeader('Content-Type', 'application/json');
	if(_message.sourceItemMainText == null){
        throw new Error('sourceItemMainText is missing from input!');
    }
  let requestBody = {
	"txt": _message.sourceItemMainText,
	"title":"",
  "query":"",
  "query_weight":0
  };
    
  let summarizerResponse = _request.Post(_remote, requestBody);
  if (!summarizerResponse.success) {
      throw new Error(summarizerResponse.error);
  }
  let response = {"sourceItemMainTextSummary": JSON.parse(summarizerResponse.text).summary};

  return response;
}
