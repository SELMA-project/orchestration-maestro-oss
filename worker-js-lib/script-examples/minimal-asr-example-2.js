var remote = "http://localhost:81/transcribe";
function getWordsWithTimes(segment) {
  if (!segment.end) {
    segment.end = segment.stop; // for compatibility with both LIA models
  }
  let words = segment.txt.split(" ");
  let timePerWord = (segment.end - segment.start) / words.length;
  let lastEnd = segment.start;
  let wordsWithTimes = [];
  for (let i = 0; i < words.length; i++) {
    let wordStart = lastEnd;
    let wordEnd = wordStart + timePerWord;
    let wordTxt = words[i].toLowerCase();
    if (i == 0) {
      if (wordTxt.length > 1) {
        wordTxt = wordTxt[0].toUpperCase() + wordTxt.slice(1, wordTxt.length);
      } else if (wordTxt.length > 0) {
        wordTxt = wordTxt.toUpperCase();
      }
    }
    if (i == words.length - 1) {
      wordEnd = segment.end;
      wordTxt += ".";
    }

    wordsWithTimes.push({
      word: wordTxt,
      start: wordStart,
      end: wordEnd,
      confidence: 0,
    });
    lastEnd = wordEnd;
  }
  return wordsWithTimes;
}
function main() {
  let audioUri = _message.VideoIngestionResult.MediaItemResourcePointers.filter(
    (p) => p.ResourceType == 1
  )[0].ObjectUri;

  let audioStream = _request.GetHttpStream(audioUri);
  let remoteResult = _request.PostFileForm(
    remote,
    `${_jobId}.wav`,
    audioStream
  );
  let remoteObject = JSON.parse(remoteResult.text).segments;

  let result = {
    TranscriptionResult: {
      ObjectType: "Transcript",
      Words: remoteObject
        .map((s) => getWordsWithTimes(s))
        .reduce((a, b) => a.concat(b), []),
    },
  };

  _logger.debug("results:" + JSON.stringify(result));
  return result;
}

function cleanup() { }
