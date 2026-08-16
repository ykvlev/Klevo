import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

import 'models.dart';

class ApiException implements Exception {
  ApiException(this.message, {this.statusCode});
  final String message;
  final int? statusCode;
  @override
  String toString() => message;
}

class ApiClient {
  ApiClient._();

  static final ApiClient instance = ApiClient._();

  static const _prefsKey = 'apiBaseUrl';
  static const _defaultBase = 'http://localhost:5178';

  String _base = _defaultBase;
  String get baseUrl => _base;

  /// Базовый URL API. На Windows desktop — http://localhost:5178,
  /// на эмуляторе Android хост-машина доступна как http://10.0.2.2:5178.
  static Future<void> loadBaseUrl() async {
    final prefs = await SharedPreferences.getInstance();
    final saved = prefs.getString(_prefsKey);
    if (saved != null && saved.isNotEmpty) {
      instance._base = saved;
    } else if (!kIsWeb && defaultTargetPlatform == TargetPlatform.android) {
      instance._base = 'http://10.0.2.2:5178';
    }
  }

  static Future<void> saveBaseUrl(String url) async {
    var normalized = url.trim().replaceAll(RegExp(r'/+$'), '');
    if (!normalized.startsWith('http://') && !normalized.startsWith('https://')) {
      normalized = 'http://$normalized';
    }
    instance._base = normalized;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_prefsKey, normalized);
  }

  Uri _uri(String path, [Map<String, String>? query]) =>
      Uri.parse('$_base$path').replace(queryParameters: query);

  Future<dynamic> _get(String path, [Map<String, String>? query]) async {
    final res = await http.get(_uri(path, query)).timeout(const Duration(seconds: 20));
    return _decode(res);
  }

  Future<dynamic> _post(String path, Map<String, dynamic> body) async {
    final res = await http
        .post(
          _uri(path),
          headers: {'Content-Type': 'application/json'},
          body: jsonEncode(body),
        )
        .timeout(const Duration(seconds: 30));
    return _decode(res);
  }

  dynamic _decode(http.Response res) {
    Object? json;
    try {
      json = jsonDecode(utf8.decode(res.bodyBytes));
    } catch (_) {
      json = null;
    }
    if (res.statusCode >= 400 || json == null) {
      final String err;
      if (json is Map && json['error'] is String) {
        err = json['error'] as String;
      } else {
        err = 'HTTP ${res.statusCode}';
      }
      throw ApiException(err, statusCode: res.statusCode);
    }
    return json;
  }

  List<Map<String, dynamic>> _asList(Object? json) =>
      (json as List?)?.whereType<Map<String, dynamic>>().toList() ?? [];

  Future<List<Spot>> getSpots() async =>
      _asList(await _get('/api/spots')).map(Spot.fromJson).toList();

  Future<Forecast> getForecast(String spotId, DateTime date) async {
    final json = await _get('/api/spots/$spotId/forecast', {'date': dateOnlyJson(date)});
    return Forecast.fromJson(json as Map<String, dynamic>);
  }

  Future<ZoneRules> getZoneRules(String zoneId) async {
    final json = await _get('/api/zones/$zoneId/rules');
    return ZoneRules.fromJson(json as Map<String, dynamic>);
  }

  Future<List<CatchItem>> getCatches(String spotId, {DateTime? from, DateTime? to}) async {
    final query = <String, String>{};
    if (from != null) query['from'] = dateOnlyJson(from);
    if (to != null) query['to'] = dateOnlyJson(to);
    final json = await _get(
      '/api/spots/$spotId/catches',
      query.isEmpty ? null : query,
    );
    return _asList(json).map(CatchItem.fromJson).toList();
  }

  Future<CatchItem> addCatch(
    String spotId, {
    required String speciesName,
    double? weightKg,
    double? lengthCm,
    String? photoUrl,
    DateTime? caughtAt,
    String? notes,
  }) async {
    final json = await _post('/api/spots/$spotId/catches', {
      'speciesName': speciesName,
      'weightKg': weightKg,
      'lengthCm': lengthCm,
      'photoUrl': photoUrl,
      'caughtAt': (caughtAt ?? DateTime.now().toUtc()).toIso8601String(),
      'notes': notes,
    });
    return CatchItem.fromJson(json as Map<String, dynamic>);
  }

  Future<List<FeedItem>> getFeed({int limit = 50}) async {
    final json = await _get('/api/catches/feed', {'limit': '$limit'});
    final items = (json as Map<String, dynamic>)['items'] as List? ?? [];
    return items
        .whereType<Map<String, dynamic>>()
        .map(FeedItem.fromJson)
        .toList();
  }

  Future<List<String>> getSpecies() async =>
      _asList(await _get('/api/species'))
          .map((m) => m['nameRu'] as String? ?? '')
          .where((s) => s.isNotEmpty)
          .toList();

  /// fish-id по base64 data-url фото (top-3 вида).
  Future<FishIdResult> fishId(String dataUrl) async {
    final res = await http
        .post(
          _uri('/api/fish-id'),
          headers: {'Content-Type': 'application/json'},
          body: jsonEncode({'dataUrl': dataUrl}),
        )
        .timeout(const Duration(seconds: 60));
    return FishIdResult.fromJson(_decode(res) as Map<String, dynamic>);
  }

  /// Загрузка фото (multipart) → относительный url вида /uploads/YYYY-MM-DD/xx.jpg.
  Future<String> uploadPhoto(Uint8List bytes, String fileName) async {
    final req = http.MultipartRequest('POST', _uri('/api/uploads'));
    req.files.add(http.MultipartFile.fromBytes('file', bytes, filename: fileName));
    final streamed = await req.send().timeout(const Duration(seconds: 60));
    final res = await http.Response.fromStream(streamed);
    final json = _decode(res) as Map<String, dynamic>;
    return json['url'] as String? ?? '';
  }

  Future<bool> health() async {
    try {
      final res = await http.get(_uri('/health')).timeout(const Duration(seconds: 5));
      return res.statusCode == 200;
    } catch (_) {
      return false;
    }
  }
}
