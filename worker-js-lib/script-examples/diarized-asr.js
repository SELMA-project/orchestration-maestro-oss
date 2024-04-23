var remote = '';

function cyrb128(str) {
    let h1 = 1779033703, h2 = 3144134277,
        h3 = 1013904242, h4 = 2773480762;
    for (let i = 0, k; i < str.length; i++) {
        k = str.charCodeAt(i);
        h1 = h2 ^ Math.imul(h1 ^ k, 597399067);
        h2 = h3 ^ Math.imul(h2 ^ k, 2869860233);
        h3 = h4 ^ Math.imul(h3 ^ k, 951274213);
        h4 = h1 ^ Math.imul(h4 ^ k, 2716044179);
    }
    h1 = Math.imul(h3 ^ (h1 >>> 18), 597399067);
    h2 = Math.imul(h4 ^ (h2 >>> 22), 2869860233);
    h3 = Math.imul(h1 ^ (h3 >>> 17), 951274213);
    h4 = Math.imul(h2 ^ (h4 >>> 19), 2716044179);
    h1 ^= (h2 ^ h3 ^ h4), h2 ^= h1, h3 ^= h1, h4 ^= h1;
    return [h1>>>0, h2>>>0, h3>>>0, h4>>>0];
}

function sfc32(a, b, c, d) {
    return function() {
      a >>>= 0; b >>>= 0; c >>>= 0; d >>>= 0; 
      var t = (a + b) | 0;
      a = b ^ b >>> 9;
      b = c + (c << 3) | 0;
      c = (c << 21 | c >>> 11);
      d = d + 1 | 0;
      t = t + d | 0;
      c = c + t | 0;
      return (t >>> 0) / 4294967296;
    }
}

function generateHexGuidFromSeed(seed) {
  const characters = '0123456789abcdef';
  
  var seed = cyrb128(seed);
  var rand = sfc32(seed[0], seed[1], seed[2], seed[3]);

  let guid = '';
  for (let i = 0; i < 36; i++) {
	let random = Math.round((rand()*100*15) % 15);
    if (i === 8 || i === 13 || i === 18 || i === 23) {
      guid += '-';
    } else {
      guid += characters[random];
    }
  }

  return guid;
}

function getWordsWithTimes(segment) {
    let wordsWithTimes = [];
	let speaker = generateHexGuidFromSeed(segment.speaker);
	let words = segment.text.split(' ').filter(e => e);
	let segmentCurrent = segment.start_time;
	let segmentJump = (segment.end_time - segment.start_time) / words.length;
	for (let j = 0; j < words.length; j++) {
		
		let wordStart = segmentCurrent;
		let wordEnd = segmentCurrent + segmentJump;

		wordsWithTimes.push({
			word: words[j],
			start: wordStart / 1000.0,
			speaker: speaker,
			end: wordEnd / 1000.0,
			confidence: 0
		});

		segmentCurrent += segmentJump;
	}
    return wordsWithTimes;
}

function main() {
	let mediaPointer =
        _message.VideoIngestionResult.MediaItemResourcePointers.filter(
            (p) => p.ResourceType == 1
        )[0];

    let audioUri = mediaPointer.ObjectUri;
	let audioDuration = mediaPointer.Metadata.Duration;

    let audioStream = _request.GetHttpStream(audioUri);
    let remoteResult = _request.PostFileForm(
     `${remote}`,
      `${_jobId}.wav`,
      audioStream
    );
	
    let remoteObject = JSON.parse(remoteResult.text);

    _logger.info('results:' + JSON.stringify(remoteObject));

    let result = {
      TranscriptionResult: {
        ObjectType: 'Transcript',
        Words: remoteObject
          .map((s) => getWordsWithTimes(s))
          .reduce((a, b) => a.concat(b), []),
      },
    };

    _logger.debug('results:' + JSON.stringify(result));

	const billing = new BillingLog({audioVideoDuration: audioDuration});
	_set_billing(billing);

    return result;
}

function cleanup() {
}