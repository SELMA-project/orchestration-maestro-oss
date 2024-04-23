const token_queue_url = 'http://localhost:9000/api/workers';
let url;

const objectFilter = (obj, callback) => Object.fromEntries(Object.entries(obj).filter(callback));

const isValueValid = ([, v]) => v !== undefined;

function add_worker(url, type, engine, version) {
    let body = objectFilter({ 'type': type, 'engine': engine, 'version': version }, isValueValid);

    _logger.debug('parsed values: ' + JSON.stringify(body));

    const response = _request.Post(token_queue_url, body);
    return response.text;
}

function acquire_worker(type, engine, version) {
    let params = objectFilter({ 'type': type, 'engine': engine, 'version': version }, isValueValid);

    _logger.debug('parsed values: ' + JSON.stringify(params));

    _request.SetParams(params);
    _request.SetHeader('Content-Type', 'application/json');
    const response = _request.Get(token_queue_url + '/acquire', null);
    return response.text;
}

function release_worker(url) {
    const response = _request.Post(token_queue_url + '/release', url);
    return response.success;
}

function gourmet_mt(url, text) {
    _logger.debug('translating text: ' + text);
    _request.SetHeader('Content-Type', 'application/json');
    let response = _request.Post(url, { q: text });
    if (!response.success) {
        throw new Error(response.error);
    }
    return JSON.parse(response.text).result;
}

function main() {
    const engine = _message.SourceLangCode + '-' + _message.TargetLangCode;
    url = acquire_worker('gourmet-mt', engine);

    let result = { 'BlockeredTranscript': null, 'SingleSegment': null }

    if (_message.SegmentedTranscript !== null) {
        const segments = _message.SegmentedTranscript.Segments;
        for (const segment of segments) {
            segment.Text = gourmet_mt(url, segment.Text);
        }
        result.BlockeredTranscript = { 'Blocks': segments };
    } else if (_message.SingleSegment !== null) {
        const text = _message.SingleSegment;
        result.SingleSegment = gourmet_mt(url, text);
    } else if (_message.BlockeredTranscript !== null) {
        throw new Error('BlockeredTranscript not implemented!');
    }

    _logger.debug('results:' + JSON.stringify(result));
    return result;
}

function cleanup() {
    release_worker(url);
}
