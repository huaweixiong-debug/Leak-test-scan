const EventEmitter = require('events');
const { SerialPort } = require('serialport');

const SCAN_IDLE_FLUSH_MS = 80;
const DEBUG_CHUNK_LIMIT = 20;

function normalizeScanText(rawValue) {
  return String(rawValue || '')
    .replace(/[\u0000-\u001f\u007f]/g, '')
    .trim();
}

function toHexString(buffer) {
  return Array.from(buffer || [])
    .map((value) => value.toString(16).padStart(2, '0').toUpperCase())
    .join(' ');
}

function toTextPreview(buffer) {
  return String((buffer || Buffer.alloc(0)).toString('utf8') || '')
    .replace(/\r/g, '<CR>')
    .replace(/\n/g, '<LF>');
}

class ScannerError extends Error {
  constructor(message, cause) {
    super(message);
    this.name = 'ScannerError';
    this.cause = cause;
  }
}

class ScannerService extends EventEmitter {
  constructor() {
    super();
    this.port = null;
    this.currentConfig = null;
    this.latestScan = null;
    this.consumedScanId = null;
    this.buffer = '';
    this.flushTimer = null;
    this.debugState = this.createDebugState();
  }

  createDebugState() {
    return {
      bytesReceived: 0,
      chunksReceived: 0,
      lastChunkAt: null,
      lastPublishedAt: null,
      modemSignals: null,
      bufferPreview: '',
      recentChunks: []
    };
  }

  async configure(config, onScan) {
    this.currentConfig = {
      comPort: config.comPort,
      baudrate: Number(config.baudrate),
      dataBits: Number(config.dataBits),
      parity: String(config.parity || 'none').toLowerCase(),
      stopBits: Number(config.stopBits),
      timeoutMs: Number(config.timeoutMs || 5000),
      dtr: config.dtr !== false,
      rts: config.rts !== false,
      enabled: Boolean(config.enabled)
    };

    if (typeof onScan === 'function') {
      this.removeAllListeners('scan');
      this.on('scan', onScan);
    }

    if (!this.currentConfig.enabled) {
      await this.disconnect();
      return { connected: false, enabled: false };
    }

    this.debugState = this.createDebugState();
    return this.connect();
  }

  async connect() {
    try {
      await this.disconnect();

      this.port = new SerialPort({
        path: this.currentConfig.comPort,
        baudRate: this.currentConfig.baudrate,
        dataBits: this.currentConfig.dataBits,
        stopBits: this.currentConfig.stopBits,
        parity: this.currentConfig.parity,
        autoOpen: false
      });

      await new Promise((resolve, reject) => {
        this.port.open((error) => {
          if (error) {
            reject(error);
            return;
          }

          resolve();
        });
      });

      await this.applyLineSignals();

      this.port.on('data', (chunk) => {
        this.handleIncomingChunk(chunk);
      });

      this.port.on('error', (error) => {
        console.error('[scanner] serial error', error);
      });

      console.log(`[scanner] connected ${this.currentConfig.comPort}`);
      return { connected: true, enabled: true };
    } catch (error) {
      console.error('[scanner] connect failed', error);
      throw new ScannerError('Scanner serial connect failed', error);
    }
  }

  async disconnect() {
    if (this.flushTimer) {
      clearTimeout(this.flushTimer);
      this.flushTimer = null;
    }

    if (!this.port) {
      return;
    }

    const port = this.port;
    this.port = null;
    this.buffer = '';
    this.debugState.bufferPreview = '';

    if (!port.isOpen) {
      return;
    }

    await new Promise((resolve) => {
      port.close(() => resolve());
    });
  }

  getLatestScan() {
    return this.latestScan;
  }

  syncLatestScan(scanEvent) {
    if (!scanEvent || !scanEvent.rawText) {
      return;
    }

    if (scanEvent.id && this.consumedScanId && scanEvent.id !== this.consumedScanId) {
      this.consumedScanId = null;
    }

    this.latestScan = {
      id: scanEvent.id || null,
      rawText: scanEvent.rawText,
      scannedAt: scanEvent.scannedAt || new Date().toISOString()
    };
    this.debugState.lastPublishedAt = this.latestScan.scannedAt;
  }

  isConsumedScan(scanEvent) {
    return Boolean(
      scanEvent &&
      scanEvent.id &&
      this.consumedScanId &&
      scanEvent.id === this.consumedScanId
    );
  }

  getLatestVisibleScan(fallbackScan = null) {
    if (this.latestScan) {
      return this.latestScan;
    }

    if (this.isConsumedScan(fallbackScan)) {
      return null;
    }

    return fallbackScan || null;
  }

  clearLatestScan(match = null) {
    if (!this.latestScan) {
      return;
    }

    if (!match) {
      this.latestScan = null;
      return;
    }

    const idMatched = match.scannerEventId && this.latestScan.id === match.scannerEventId;
    const qrMatched = !this.latestScan.id && match.qrCode && this.latestScan.rawText === match.qrCode;
    if (idMatched || qrMatched) {
      this.latestScan = null;
    }
  }

  markScanConsumed(match = null) {
    if (match && match.scannerEventId) {
      this.consumedScanId = match.scannerEventId;
    }

    this.clearLatestScan(match);
  }

  consumeCurrentScan(match = null) {
    if (match && match.scannerEventId) {
      this.consumedScanId = match.scannerEventId;
    } else if (this.latestScan && this.latestScan.id) {
      this.consumedScanId = this.latestScan.id;
    }

    this.latestScan = null;
    this.buffer = '';
    this.debugState.bufferPreview = '';
  }

  getDebugState() {
    return {
      connected: this.isConnected(),
      latestScan: this.latestScan,
      currentConfig: this.currentConfig,
      debug: {
        ...this.debugState,
        recentChunks: this.debugState.recentChunks.slice()
      }
    };
  }

  async updateLineSignals(options) {
    if (!this.currentConfig) {
      throw new ScannerError('Scanner config not initialized');
    }

    this.currentConfig.dtr = Boolean(options.dtr);
    this.currentConfig.rts = Boolean(options.rts);

    if (options.reconnect !== false) {
      await this.connect();
    } else if (this.port && this.port.isOpen) {
      await this.applyLineSignals();
    }

    return this.getDebugState();
  }

  isConnected() {
    return Boolean(this.port && this.port.isOpen);
  }

  async applyLineSignals() {
    await new Promise((resolve, reject) => {
      this.port.set({
        dtr: this.currentConfig.dtr !== false,
        rts: this.currentConfig.rts !== false,
        brk: false
      }, (error) => {
        if (error) {
          reject(error);
          return;
        }

        resolve();
      });
    });

    await new Promise((resolve) => {
      this.port.get((error, status) => {
        if (error) {
          console.error('[scanner] get modem status failed', error);
          resolve();
          return;
        }

        this.debugState.modemSignals = status;
        resolve();
      });
    });
  }

  handleIncomingChunk(chunk) {
    const bufferChunk = Buffer.isBuffer(chunk)
      ? chunk
      : Buffer.from(String(chunk || ''), 'utf8');
    const textChunk = Buffer.isBuffer(chunk)
      ? chunk.toString('utf8')
      : String(chunk || '');

    this.captureDebugChunk(bufferChunk);
    this.buffer += textChunk;
    this.debugState.bufferPreview = this.buffer
      .replace(/\r/g, '<CR>')
      .replace(/\n/g, '<LF>')
      .slice(-200);

    let boundaryIndex = this.findBoundaryIndex(this.buffer);
    while (boundaryIndex !== -1) {
      const payload = this.buffer.slice(0, boundaryIndex);
      this.buffer = this.buffer.slice(boundaryIndex + 1);
      this.publishScan(payload);
      boundaryIndex = this.findBoundaryIndex(this.buffer);
    }

    if (this.flushTimer) {
      clearTimeout(this.flushTimer);
    }

    this.flushTimer = setTimeout(() => {
      const payload = this.buffer;
      this.buffer = '';
      this.debugState.bufferPreview = '';
      this.publishScan(payload);
    }, SCAN_IDLE_FLUSH_MS);
  }

  captureDebugChunk(bufferChunk) {
    this.debugState.bytesReceived += bufferChunk.length;
    this.debugState.chunksReceived += 1;
    this.debugState.lastChunkAt = new Date().toISOString();
    this.debugState.recentChunks.push({
      at: this.debugState.lastChunkAt,
      size: bufferChunk.length,
      hex: toHexString(bufferChunk),
      textPreview: toTextPreview(bufferChunk)
    });

    if (this.debugState.recentChunks.length > DEBUG_CHUNK_LIMIT) {
      this.debugState.recentChunks = this.debugState.recentChunks.slice(-DEBUG_CHUNK_LIMIT);
    }
  }

  findBoundaryIndex(bufferText) {
    if (!bufferText) {
      return -1;
    }

    const carriageReturn = bufferText.indexOf('\r');
    const lineFeed = bufferText.indexOf('\n');

    if (carriageReturn === -1) {
      return lineFeed;
    }

    if (lineFeed === -1) {
      return carriageReturn;
    }

    return Math.min(carriageReturn, lineFeed);
  }

  publishScan(rawValue) {
    const rawText = normalizeScanText(rawValue);
    if (!rawText) {
      return;
    }

    this.latestScan = {
      rawText,
      scannedAt: new Date().toISOString()
    };
    this.debugState.lastPublishedAt = this.latestScan.scannedAt;

    console.log(`[scanner] scan received ${rawText}`);
    this.emit('scan', this.latestScan);
  }
}

module.exports = {
  ScannerError,
  scannerService: new ScannerService()
};
