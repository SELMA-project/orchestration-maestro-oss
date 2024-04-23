function mt (src) {
    let invstr = ""
    for(let i = src.length - 1; i >= 0  ; i--) {
        invstr += src[i];
    }
    return invstr;
}
function main() {
    const engine = _message.SourceLangCode + '-' + _message.TargetLangCode;

    let result = {'BlockeredTranscript': null, 'SingleSegment': null}
    
    if (_message.SegmentedTranscript !== null) {
        const segments = _message.SegmentedTranscript.Segments;
        for (const segment of segments) {
            segment.Text = mt(segment.Text);
        }
        result.BlockeredTranscript = {'Blocks': segments};
    } else if(_message.SingleSegment !== null){
        const text = _message.SingleSegment;
        result.SingleSegment = mt(text);
    } else if(_message.BlockeredTranscript !== null){
        throw new Error('BlockeredTranscript not implemented!');
    }
    
    _logger.debug('results:' + JSON.stringify(result));
    return result;
}
function cleanup() {
}
