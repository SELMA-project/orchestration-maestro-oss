var remoteAddr = "7040f73ca1175164c2c6aa6ef4a154aca163c9acc2e0450ffbebb55d0e76ddda";
var l0 = `http://${remoteAddr}/x:selmaproject:gourmet:`;
var l1 = ':4000/translation';

function mt(text, engine) {
  var remote = `${l0}${engine}${l1}`;
  _logger.info(`[${_jobId}] ${remote} | { 'q': '${text}' } `);
  _request.SetHeader('Content-Type', 'application/json');
  let response = _request.Post(remote, { q: text });
  if (!response.success) {
      throw new Error(response.error);
  }
  return JSON.parse(response.text).result;
}

function main() {
    const engine =
      _message.SourceLangCode + '-' + _message.TargetLangCode;
    let result = {'BlockeredTranscript': null, 'SingleSegment': null}
   
    if (_message.SegmentedTranscript !== null) {
        const segments = _message.SegmentedTranscript.Segments;
        for (const segment of segments) {
            segment.Text = mt(segment.Text, engine);
        }
        result.BlockeredTranscript = {'Blocks': segments};
    } else if(_message.SingleSegment !== null){
        const text = _message.SingleSegment;
        result.SingleSegment = mt(text, engine);
    } else if(_message.BlockeredTranscript !== null){
        throw new Error('BlockeredTranscript not implemented!');
    }
    
    _logger.debug('results:' + JSON.stringify(result));
    return result;
}