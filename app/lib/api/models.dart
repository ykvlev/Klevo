class Spot {
  Spot({
    required this.id,
    required this.name,
    required this.waterType,
    required this.region,
    required this.zoneId,
    required this.lat,
    required this.lon,
  });

  final String id;
  final String name;
  final String waterType;
  final String region;
  final String zoneId;
  final double lat;
  final double lon;

  factory Spot.fromJson(Map<String, dynamic> j) => Spot(
        id: j['id'] as String,
        name: j['name'] as String? ?? '',
        waterType: j['waterType'] as String? ?? '',
        region: j['region'] as String? ?? '',
        zoneId: j['zoneId'] as String? ?? '',
        lat: (j['lat'] as num).toDouble(),
        lon: (j['lon'] as num).toDouble(),
      );
}

class Forecast {
  Forecast({
    required this.spotId,
    required this.date,
    required this.score,
    required this.bestStart,
    required this.bestEnd,
    required this.modelVersion,
    required this.dataConfidence,
    required this.sources,
    required this.satellite,
    required this.dataNote,
  });

  final String spotId;
  final String date;
  final int score;
  final String bestStart;
  final String bestEnd;
  final String modelVersion;
  final int dataConfidence;
  final List<String> sources;
  final List<String> satellite;
  final String dataNote;

  bool get isMl => modelVersion == 'ml-v1';

  String get sourceLabel => isMl ? 'ML-МОДЕЛЬ' : 'ПРАВИЛА';

  factory Forecast.fromJson(Map<String, dynamic> j) => Forecast(
        spotId: j['spotId'] as String? ?? '',
        date: j['date'] as String? ?? '',
        score: (j['score'] as num?)?.toInt() ?? 0,
        bestStart: j['bestStart'] as String? ?? '--:--',
        bestEnd: j['bestEnd'] as String? ?? '--:--',
        modelVersion: j['modelVersion'] as String? ?? '',
        dataConfidence: (j['dataConfidence'] as num?)?.toInt() ?? 0,
        sources: _strList(j['sources'], 'label'),
        satellite: _strList(j['satellite'], 'label'),
        dataNote: j['dataNote'] as String? ?? '',
      );

  static List<String> _strList(dynamic list, String key) {
    if (list is! List) return [];
    return list
        .whereType<Map<String, dynamic>>()
        .map((m) => (m[key] ?? '').toString())
        .where((s) => s.isNotEmpty)
        .toList();
  }
}

class MinSize {
  MinSize({required this.species, required this.minSizeCm});
  final String species;
  final double minSizeCm;
  factory MinSize.fromJson(Map<String, dynamic> j) => MinSize(
        species: j['species'] as String? ?? '',
        minSizeCm: (j['minSizeCm'] as num?)?.toDouble() ?? 0,
      );
}

class DailyLimit {
  DailyLimit({required this.species, required this.value, required this.unit});
  final String species;
  final double value;
  final String unit;
  factory DailyLimit.fromJson(Map<String, dynamic> j) => DailyLimit(
        species: j['species'] as String? ?? '',
        value: (j['value'] as num?)?.toDouble() ?? 0,
        unit: j['unit'] as String? ?? '',
      );
}

class Ban {
  Ban({
    required this.type,
    required this.species,
    required this.periodFrom,
    required this.periodTo,
    required this.periodRule,
    required this.area,
    required this.ruleText,
    required this.permanent,
  });
  final String type;
  final String species;
  final String periodFrom;
  final String periodTo;
  final String periodRule;
  final String area;
  final String ruleText;
  final bool permanent;

  String get label {
    if (permanent) return 'ЗАПРЕТ';
    if (periodRule.isNotEmpty) return periodRule;
    if (periodFrom.isNotEmpty && periodTo.isNotEmpty) {
      return 'ЗАПРЕТ ${periodFrom.substring(5)}–${periodTo.substring(5)}';
    }
    return 'ЗАПРЕТ';
  }

  factory Ban.fromJson(Map<String, dynamic> j) => Ban(
        type: j['type'] as String? ?? '',
        species: j['species'] as String? ?? '',
        periodFrom: j['periodFrom'] as String? ?? '',
        periodTo: j['periodTo'] as String? ?? '',
        periodRule: j['periodRule'] as String? ?? '',
        area: j['area'] as String? ?? '',
        ruleText: j['ruleText'] as String? ?? '',
        permanent: j['permanent'] as bool? ?? false,
      );
}

class ZoneRules {
  ZoneRules({
    required this.zoneName,
    required this.minSizes,
    required this.dailyLimits,
    required this.defaultDailyLimitKg,
    required this.defaultLimitNote,
    required this.bans,
  });
  final String zoneName;
  final List<MinSize> minSizes;
  final List<DailyLimit> dailyLimits;
  final double? defaultDailyLimitKg;
  final String defaultLimitNote;
  final List<Ban> bans;

  factory ZoneRules.fromJson(Map<String, dynamic> j) {
    final zone = j['zone'] as Map<String, dynamic>? ?? const {};
    return ZoneRules(
      zoneName: zone['name'] as String? ?? '',
      minSizes: (j['minSizes'] as List? ?? [])
          .whereType<Map<String, dynamic>>()
          .map(MinSize.fromJson)
          .toList(),
      dailyLimits: (j['dailyLimits'] as List? ?? [])
          .whereType<Map<String, dynamic>>()
          .map(DailyLimit.fromJson)
          .toList(),
      defaultDailyLimitKg: (j['defaultDailyLimitKg'] as num?)?.toDouble(),
      defaultLimitNote: j['defaultLimitNote'] as String? ?? '',
      bans: (j['bans'] as List? ?? [])
          .whereType<Map<String, dynamic>>()
          .map(Ban.fromJson)
          .toList(),
    );
  }
}

class CatchItem {
  CatchItem({
    required this.id,
    required this.speciesName,
    required this.weightKg,
    required this.lengthCm,
    required this.photoUrl,
    required this.caughtAt,
    required this.notes,
  });
  final String id;
  final String speciesName;
  final double? weightKg;
  final double? lengthCm;
  final String? photoUrl;
  final DateTime caughtAt;
  final String? notes;

  String get dateLabel {
    final l = caughtAt.toLocal();
    return '${l.day.toString().padLeft(2, '0')}.${l.month.toString().padLeft(2, '0')}.${l.year}';
  }

  factory CatchItem.fromJson(Map<String, dynamic> j) => CatchItem(
        id: j['id'] as String,
        speciesName: j['speciesName'] as String? ?? '',
        weightKg: (j['weightKg'] as num?)?.toDouble(),
        lengthCm: (j['lengthCm'] as num?)?.toDouble(),
        photoUrl: j['photoUrl'] as String?,
        caughtAt: DateTime.tryParse(j['caughtAt'] as String? ?? '')?.toUtc() ??
            DateTime.now().toUtc(),
        notes: j['notes'] as String?,
      );
}

class RuleInfo {
  RuleInfo({
    required this.allowed,
    required this.zoneName,
    required this.violations,
    required this.summary,
  });
  final bool allowed;
  final String zoneName;
  final int violations;
  final String summary;

  factory RuleInfo.fromJson(Map<String, dynamic>? j) {
    if (j == null) return RuleInfo(allowed: true, zoneName: '', violations: 0, summary: '');
    return RuleInfo(
      allowed: j['allowed'] as bool? ?? true,
      zoneName: j['zoneName'] as String? ?? '',
      violations: (j['violations'] as num?)?.toInt() ?? 0,
      summary: j['summary'] as String? ?? '',
    );
  }
}

class FeedItem {
  FeedItem({
    required this.id,
    required this.spotName,
    required this.waterType,
    required this.speciesName,
    required this.weightKg,
    required this.lengthCm,
    required this.photoUrl,
    required this.caughtAt,
    required this.notes,
    required this.rule,
  });
  final String id;
  final String spotName;
  final String waterType;
  final String speciesName;
  final double? weightKg;
  final double? lengthCm;
  final String? photoUrl;
  final DateTime caughtAt;
  final String? notes;
  final RuleInfo rule;

  factory FeedItem.fromJson(Map<String, dynamic> j) => FeedItem(
        id: j['id'] as String,
        spotName: j['spotName'] as String? ?? '',
        waterType: j['waterType'] as String? ?? '',
        speciesName: j['speciesName'] as String? ?? '',
        weightKg: (j['weightKg'] as num?)?.toDouble(),
        lengthCm: (j['lengthCm'] as num?)?.toDouble(),
        photoUrl: j['photoUrl'] as String?,
        caughtAt: DateTime.tryParse(j['caughtAt'] as String? ?? '')?.toUtc() ??
            DateTime.now().toUtc(),
        notes: j['notes'] as String?,
        rule: RuleInfo.fromJson(j['rule'] as Map<String, dynamic>?),
      );
}

class FishIdPrediction {
  FishIdPrediction({
    required this.speciesId,
    required this.nameRu,
    required this.nameLatin,
    required this.confidence,
  });
  final String? speciesId;
  final String nameRu;
  final String nameLatin;
  final double confidence;

  factory FishIdPrediction.fromJson(Map<String, dynamic> j) => FishIdPrediction(
        speciesId: j['speciesId'] as String?,
        nameRu: j['nameRu'] as String? ?? '',
        nameLatin: j['nameLatin'] as String? ?? '',
        confidence: (j['confidence'] as num?)?.toDouble() ?? 0,
      );
}

class FishIdResult {
  FishIdResult({required this.modelVersion, required this.top});
  final String modelVersion;
  final List<FishIdPrediction> top;

  factory FishIdResult.fromJson(Map<String, dynamic> j) => FishIdResult(
        modelVersion: j['modelVersion'] as String? ?? '',
        top: (j['top'] as List? ?? [])
            .whereType<Map<String, dynamic>>()
            .map(FishIdPrediction.fromJson)
            .toList(),
      );
}

String dateOnlyJson(DateTime d) =>
    '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';
