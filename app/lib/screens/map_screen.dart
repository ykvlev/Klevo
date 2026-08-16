import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../api/api_client.dart';
import '../api/models.dart';
import '../theme.dart';
import '../widgets/forecast_card.dart';
import '../widgets/rules_card.dart';

const _tileUrl = 'https://a.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}.png';

class MapScreen extends StatefulWidget {
  const MapScreen({super.key});

  @override
  State<MapScreen> createState() => _MapScreenState();
}

class _MapScreenState extends State<MapScreen> {
  final _mapController = MapController();
  List<Spot> _spots = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final spots = await ApiClient.instance.getSpots();
      if (!mounted) return;
      setState(() => _spots = spots);
    } on Exception catch (e) {
      if (!mounted) return;
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  LatLng _center() {
    if (_spots.isEmpty) return const LatLng(60.0, 31.0);
    final lat = _spots.map((s) => s.lat).reduce((a, b) => a + b) / _spots.length;
    final lon = _spots.map((s) => s.lon).reduce((a, b) => a + b) / _spots.length;
    return LatLng(lat, lon);
  }

  Future<void> _openSpot(Spot spot) async {
    showModalBottomSheet(
      context: context,
      backgroundColor: AppColors.canvas,
      isScrollControlled: true,
      builder: (_) => _SpotSheet(spot: spot),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('КАРТА ТОЧЕК'),
        actions: [
          Padding(
            padding: const EdgeInsets.only(right: 16),
            child: IconButton(
              tooltip: 'Обновить',
              onPressed: _load,
              icon: const Icon(Icons.refresh),
            ),
          ),
        ],
      ),
      body: _buildBody(),
    );
  }

  Widget _buildBody() {
    if (_loading) return const Center(child: CircularProgressIndicator());
    if (_error != null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.cloud_off, size: 48, color: AppColors.textMuted),
              const SizedBox(height: 12),
              Text('$_error', textAlign: TextAlign.center,
                  style: const TextStyle(color: AppColors.textSecondary)),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: _load,
                style: FilledButton.styleFrom(backgroundColor: AppColors.accent),
                child: const Text('ПОВТОРИТЬ'),
              ),
            ],
          ),
        ),
      );
    }

    return Stack(
      children: [
        FlutterMap(
          mapController: _mapController,
          options: MapOptions(
            initialCenter: _center(),
            initialZoom: 8,
            backgroundColor: AppColors.canvas,
            minZoom: 4,
            maxZoom: 17,
          ),
          children: [
            TileLayer(
              urlTemplate: _tileUrl,
              userAgentPackageName: 'dev.klevo.klevo',
            ),
            MarkerLayer(
              markers: [
                for (final s in _spots)
                  Marker(
                    point: LatLng(s.lat, s.lon),
                    width: 34,
                    height: 34,
                    child: GestureDetector(
                      onTap: () => _openSpot(s),
                      child: _Pin(
                        color: waterColor(s.waterType),
                        name: s.name,
                      ),
                    ),
                  ),
              ],
            ),
          ],
        ),
        Positioned(
          left: 12,
          bottom: 12,
          child: Card(
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
              child: Text(
                '${_spots.length} точек',
                style: const TextStyle(fontFamily: 'RobotoMono', fontSize: 11, color: AppColors.textSecondary),
              ),
            ),
          ),
        ),
      ],
    );
  }
}

Color waterColor(String type) {
  final t = type.toLowerCase();
  if (t.contains('река') || t.contains('канал')) return const Color(0xFF4A9BD9);
  if (t.contains('озеро')) return const Color(0xFFE0A03C);
  if (t.contains('водохранилищ')) return const Color(0xFF6A5ACD);
  if (t.contains('море') || t.contains('залив') || t.contains('зал')) return const Color(0xFF2E9E7B);
  return AppColors.accent;
}

class _Pin extends StatelessWidget {
  const _Pin({required this.color, required this.name});
  final Color color;
  final String name;

  @override
  Widget build(BuildContext context) {
    return Tooltip(
      message: name,
      child: Center(
        child: Container(
          width: 20,
          height: 20,
          decoration: BoxDecoration(
            color: color,
            shape: BoxShape.circle,
            border: Border.all(color: AppColors.canvas, width: 2),
            boxShadow: const [
              BoxShadow(color: Colors.black54, blurRadius: 6, offset: Offset(0, 2)),
            ],
          ),
        ),
      ),
    );
  }
}

class _SpotSheet extends StatefulWidget {
  const _SpotSheet({required this.spot});
  final Spot spot;

  @override
  State<_SpotSheet> createState() => _SpotSheetState();
}

class _SpotSheetState extends State<_SpotSheet> {
  Forecast? _forecast;
  ZoneRules? _rules;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _forecast = null;
      _rules = null;
      _error = null;
    });
    try {
      final results = await Future.wait([
        ApiClient.instance.getForecast(widget.spot.id, DateTime.now()),
        ApiClient.instance.getZoneRules(widget.spot.zoneId),
      ]);
      if (!mounted) return;
      setState(() {
        _forecast = results[0] as Forecast;
        _rules = results[1] as ZoneRules;
      });
    } on Exception catch (e) {
      if (!mounted) return;
      setState(() => _error = e.toString());
    }
  }

  @override
  Widget build(BuildContext context) {
    final spot = widget.spot;
    return DraggableScrollableSheet(
      expand: false,
      initialChildSize: 0.55,
      maxChildSize: 0.85,
      builder: (context, scrollController) {
        return ListView(
          controller: scrollController,
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 32),
          children: [
            Row(
              children: [
                Container(
                  width: 12,
                  height: 12,
                  decoration: BoxDecoration(
                    color: waterColor(spot.waterType),
                    shape: BoxShape.circle,
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(spot.name,
                          style: Theme.of(context).textTheme.titleLarge),
                      Text(
                        '${spot.waterType} · ${spot.region}',
                        style: const TextStyle(
                          fontFamily: 'RobotoMono',
                          fontSize: 11,
                          color: AppColors.textMuted,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 16),
            if (_error != null)
              Text(_error!, style: const TextStyle(color: AppColors.bad))
            else if (_forecast == null)
              const Center(
                child: Padding(
                  padding: EdgeInsets.all(24),
                  child: CircularProgressIndicator(),
                ),
              )
            else ...[
              ForecastCard(forecast: _forecast!),
              const SizedBox(height: 12),
              if (_rules != null) RulesCard(rules: _rules!),
              const SizedBox(height: 20),
              FilledButton.icon(
                onPressed: () => Navigator.of(context).pop(),
                style: FilledButton.styleFrom(backgroundColor: AppColors.accent),
                icon: const Icon(Icons.edit_note),
                label: const Text('ОТКРЫТЬ В ЖУРНАЛЕ'),
              ),
            ],
          ],
        );
      },
    );
  }
}
