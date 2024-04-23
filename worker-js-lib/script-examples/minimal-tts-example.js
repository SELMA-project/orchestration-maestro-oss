var voice = 'philip%20verminnen';
var remote = 'http://localhost:82/api/tts';
//var pydub = 'http://host.docker.internal:9094/duration'; // http://pydub:8000/duration
var pydub = 'http://pydub:8000/duration';
// Test GUI at http://localhost:82/'

// [{},{},{},{}, ...] => {k1:[{}], k2:[{},{},{}], ...}
function groupBy(arr, key) {
  return arr.reduce(function (rv, x) {
    (rv[x[key]] = rv[x[key]] || []).push(x);
    return rv;
  }, {});
};

function main() {
  let result = { VoiceoverResult: null };

  if (_message.SegmentedTranscript !== null) {
    const segments = _message.SegmentedTranscript.Segments;
    result.VoiceoverResult = {
      VoiceOverB64segments: []
    };
    const blocks = groupBy(segments, 'BlockId');
    for (const blockId of Object.keys(blocks)) {
      let block = blocks[blockId];
      let startTime = block[0].Start;
      let endTime = block[block.length - 1].End;
      let blockText = block
        .map((a) => a.Text)
        .reduce((r, x) => {
          if (r != '')
            r += ' ';
          r += x.replace(/\n/g, ' ')
            .replace(/\<speak\>/, '')
            .replace(/\<\/speak\>/, '');
          return r;
        }, '');
      let encodedText = encodeURIComponent(blockText);
      _request.RemoveHeader('Content-Type');
      let wav64 = _request.GetHttpStream(
        `${remote}?text=${encodedText}&speaker_id=${voice}`,
        true /*reads to base64*/
      );
      _request.SetHeader('Content-Type', 'application/json');
      let durationObject = _request.Post(pydub, { audioBytes64: wav64 });
      let durationMs = JSON.parse(durationObject.text)['duration_ms'];
      result.VoiceoverResult.VoiceOverB64segments.push({
        TranscriptSegment: {
          Start: startTime,
          End: endTime,
          Text: blockText,
          BlockId: _guid(),
        },
        b64Voice: wav64,
        Duration: durationMs,
      });
    }
  } else if (_message.SingleSegment !== null) {
    let cleanText = _message.SingleSegment
      .replace(/\n/g, ' ')
      .replace(/\<speak\>/, '')
      .replace(/\<\/speak\>/, '');
    let encodedText = encodeURIComponent(cleanText);
    _request.RemoveHeader('Content-Type');
    let wav64 = _request.GetHttpStream(
      `${remote}?text=${encodedText}&speaker_id=${voice}`,
      true /*reads to base64*/
    );
    _request.SetHeader('Content-Type', 'application/json');
    let durationObject = _request.Post(pydub, { audioBytes64: wav64 });
    let durationMs = JSON.parse(durationObject.text)['duration_ms'];
    result.VoiceoverResult = {
      VoiceOverB64segment: {
        // Not used in single-segment result
        TranscriptSegment: {
          Start: 0.0,
          End: 0.0,
          Text: '',
          BlockId: '00000000-0000-0000-0000-000000000000'
        },
        b64Voice: wav64,
        Duration: durationMs
      }
    };
  } else if (_message.BlockeredTranscript !== null) {
    throw new Error('BlockeredTranscript not implemented!');
  }

  _logger.debug('results:' + JSON.stringify(result));
  return result;
}

function cleanup() {
}
